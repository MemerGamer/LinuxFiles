// Copyright (c) Files Community
// Licensed under the MIT License.

#![deny(unsafe_op_in_unsafe_fn)]
#[cfg(not(all(
    target_os = "linux",
    any(target_arch = "x86_64", target_arch = "aarch64")
)))]
compile_error!("The benchmark supports Linux x86_64 and aarch64 only.");

mod ffi;

use std::ffi::CStr;
use std::os::fd::{AsRawFd, OwnedFd};

const BUFFER_SIZE: usize = 65536;
const STAT_MASK: u32 = 0x134b;
const EIO: i32 = 5;
const ERANGE: i32 = 34;

#[derive(Clone, Copy, Default)]
#[repr(C)]
pub struct Record {
    pub size: u64,
    pub inode: u64,
    pub modified_seconds: i64,
    pub mount_id: u64,
    pub mode: u32,
    pub owner_id: u32,
    pub modified_nanoseconds: u32,
    pub dev_major: u32,
    pub dev_minor: u32,
    pub mask: u32,
    pub name_offset: u32,
    pub name_length: u32,
}

pub struct Scanner {
    fd: OwnedFd,
    buffer: Box<[u8; BUFFER_SIZE]>,
    offset: usize,
    used: usize,
}

fn dirent(bytes: &[u8]) -> Result<(usize, usize), i32> {
    if bytes.len() < 24 {
        return Err(EIO);
    }
    let length = u16::from_le_bytes([bytes[16], bytes[17]]) as usize;
    if length < 24 || length > bytes.len() || length % 8 != 0 {
        return Err(EIO);
    }
    let name = &bytes[19..length];
    let end = name.iter().position(|b| *b == 0).ok_or(EIO)?;
    if end == 0 || name[..end].contains(&b'/') {
        return Err(EIO);
    }
    Ok((length, end))
}

fn u32_at(bytes: &[u8; 256], offset: usize) -> u32 {
    u32::from_le_bytes(bytes[offset..offset + 4].try_into().unwrap())
}

fn u64_at(bytes: &[u8; 256], offset: usize) -> u64 {
    u64::from_le_bytes(bytes[offset..offset + 8].try_into().unwrap())
}

fn parse_stat(bytes: &[u8; 256]) -> Result<Record, i32> {
    let mask = u32_at(bytes, 0);
    if mask & 0xb != 0xb {
        return Err(EIO);
    }
    Ok(Record {
        size: if mask & 0x200 != 0 {
            u64_at(bytes, 40)
        } else {
            0
        },
        inode: if mask & 0x100 != 0 {
            u64_at(bytes, 32)
        } else {
            0
        },
        modified_seconds: if mask & 0x40 != 0 {
            u64_at(bytes, 112) as i64
        } else {
            0
        },
        modified_nanoseconds: if mask & 0x40 != 0 {
            u32_at(bytes, 120)
        } else {
            0
        },
        mount_id: if mask & 0x1000 != 0 {
            u64_at(bytes, 144)
        } else {
            0
        },
        mode: u16::from_le_bytes([bytes[28], bytes[29]]) as u32,
        owner_id: u32_at(bytes, 20),
        dev_major: u32_at(bytes, 136),
        dev_minor: u32_at(bytes, 140),
        mask,
        ..Record::default()
    })
}

impl Scanner {
    fn open(path: &CStr) -> Result<Self, i32> {
        Ok(Self {
            fd: ffi::open(path)?,
            buffer: Box::new([0; BUFFER_SIZE]),
            offset: 0,
            used: 0,
        })
    }

    fn next(&mut self, records: &mut [Record], names: &mut [u8]) -> Result<(usize, usize), i32> {
        let mut count = 0;
        let mut name_used = 0;
        while count < records.len() {
            if self.offset == self.used {
                self.used = ffi::getdents(self.fd.as_raw_fd(), self.buffer.as_mut_slice())?;
                self.offset = 0;
                if self.used == 0 {
                    break;
                }
            }
            let bytes = &self.buffer[self.offset..self.used];
            let (length, name_length) = dirent(bytes)?;
            let name = &bytes[19..19 + name_length];
            if name == b"." || name == b".." {
                self.offset += length;
                continue;
            }
            if name_used + name_length > names.len() {
                if count == 0 {
                    return Err(ERANGE);
                }
                break;
            }
            let c_name =
                CStr::from_bytes_with_nul(&bytes[19..20 + name_length]).map_err(|_| EIO)?;
            let stat = ffi::stat(self.fd.as_raw_fd(), c_name, STAT_MASK)?;
            let mut record = parse_stat(&stat)?;
            record.name_offset = name_used as u32;
            record.name_length = name_length as u32;
            records[count] = record;
            names[name_used..name_used + name_length].copy_from_slice(name);
            name_used += name_length;
            count += 1;
            self.offset += length;
        }
        Ok((count, name_used))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_malformed_records() {
        let mut bytes = [0; 32];
        assert!(dirent(&bytes[..23]).is_err());
        for length in [0u16, 19, 25, 40] {
            bytes[16..18].copy_from_slice(&length.to_le_bytes());
            assert!(dirent(&bytes).is_err());
        }
        bytes[16..18].copy_from_slice(&32u16.to_le_bytes());
        assert!(dirent(&bytes).is_err());
        bytes[19..].fill(b'a');
        assert!(dirent(&bytes).is_err());
        bytes[20] = 0;
        assert_eq!(dirent(&bytes), Ok((32, 1)));
        bytes[19] = b'/';
        assert!(dirent(&bytes).is_err());
    }

    #[test]
    fn fixed_layout_and_optional_stat_fields() {
        assert_eq!(std::mem::size_of::<Record>(), 64);
        assert_eq!(std::mem::offset_of!(Record, name_offset), 56);
        let mut bytes = [0; 256];
        assert!(parse_stat(&bytes).is_err());
        bytes[0] = 0xb;
        bytes[40..48].fill(255);
        let record = parse_stat(&bytes).unwrap();
        assert_eq!(record.size, 0);
        assert_eq!(record.mount_id, 0);
    }

    #[test]
    fn name_arena_backpressure_does_not_lose_entry() {
        let path = std::env::temp_dir().join(format!("lfc-test-{}", std::process::id()));
        std::fs::create_dir(&path).unwrap();
        std::fs::write(path.join("one"), b"x").unwrap();
        let c_path = std::ffi::CString::new(path.as_os_str().as_encoded_bytes()).unwrap();
        let mut scanner = Scanner::open(&c_path).unwrap();
        let mut records = [Record::default(); 1];
        assert_eq!(scanner.next(&mut records, &mut [0; 1]), Err(ERANGE));
        let mut names = [0; 3];
        assert_eq!(scanner.next(&mut records, &mut names), Ok((1, 3)));
        assert_eq!(&names, b"one");
        assert_eq!(records[0].size, 1);
        assert_eq!(scanner.next(&mut records, &mut names), Ok((0, 0)));
        drop(scanner);
        std::fs::remove_file(path.join("one")).unwrap();
        std::fs::remove_dir(path).unwrap();
    }
}
