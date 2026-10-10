// Copyright (c) Files Community
// Licensed under the MIT License.

// All unsafe code is confined to libc and C ABI boundaries in this module.
use crate::{Record, Scanner};
use std::ffi::{CStr, c_char, c_int, c_void};
use std::mem::size_of;
use std::os::fd::{FromRawFd, OwnedFd};
use std::panic::{AssertUnwindSafe, catch_unwind};

unsafe extern "C" {
    fn openat(fd: c_int, path: *const c_char, flags: c_int, mode: u32) -> c_int;
    fn getdents64(fd: c_int, buffer: *mut c_void, length: usize) -> isize;
    fn statx(fd: c_int, name: *const c_char, flags: c_int, mask: u32, stat: *mut c_void) -> c_int;
    fn __errno_location() -> *mut c_int;
}

fn errno() -> i32 {
    // SAFETY: libc supplies a valid thread-local errno pointer.
    unsafe { *__errno_location() }
}

pub fn open(path: &CStr) -> Result<OwnedFd, i32> {
    #[cfg(target_arch = "x86_64")]
    const FLAGS: i32 = 0x80000 | 0x10000 | 0x20000;
    #[cfg(target_arch = "aarch64")]
    const FLAGS: i32 = 0x80000 | 0x4000 | 0x8000;
    // SAFETY: path is NUL-terminated; the fourth argument is unused without O_CREAT.
    let fd = unsafe { openat(-100, path.as_ptr(), FLAGS, 0) };
    if fd < 0 {
        return Err(errno());
    }
    // SAFETY: openat returned a new descriptor; OwnedFd is its only owner.
    Ok(unsafe { OwnedFd::from_raw_fd(fd) })
}

pub fn getdents(fd: i32, buffer: &mut [u8]) -> Result<usize, i32> {
    loop {
        // SAFETY: libc writes at most buffer.len() bytes to this live mutable slice.
        let length = unsafe { getdents64(fd, buffer.as_mut_ptr().cast(), buffer.len()) };
        if length >= 0 {
            let length = length as usize;
            return if length <= buffer.len() {
                Ok(length)
            } else {
                Err(5)
            };
        }
        let error = errno();
        if error != 4 {
            return Err(error);
        }
    }
}

pub fn stat(fd: i32, name: &CStr, mask: u32) -> Result<[u8; 256], i32> {
    #[repr(C, align(8))]
    struct Buffer([u8; 256]);
    let mut buffer = Buffer([0; 256]);
    loop {
        // SAFETY: statx uses the Linux 256-byte layout; buffer is aligned and name is terminated.
        let status = unsafe { statx(fd, name.as_ptr(), 0x100, mask, buffer.0.as_mut_ptr().cast()) };
        if status == 0 {
            return Ok(buffer.0);
        }
        let error = errno();
        if error != 4 {
            return Err(error);
        }
    }
}

fn boundary(action: impl FnOnce() -> Result<(), i32>) -> i32 {
    match catch_unwind(AssertUnwindSafe(action)) {
        Ok(Ok(())) => 0,
        Ok(Err(error)) => error,
        Err(_) => 5,
    }
}

/// # Safety
/// path must be NUL-terminated; handle must point to writable pointer storage.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn lfc_open(path: *const c_char, handle: *mut *mut Scanner) -> i32 {
    boundary(|| {
        if path.is_null() || handle.is_null() {
            return Err(22);
        }
        // SAFETY: the caller guarantees both pointers; clear output even on failure.
        unsafe { *handle = std::ptr::null_mut() };
        let scanner = Scanner::open(unsafe { CStr::from_ptr(path) })?;
        // SAFETY: transfer ownership of this Box to the caller until lfc_close.
        unsafe { *handle = Box::into_raw(Box::new(scanner)) };
        Ok(())
    })
}

/// # Safety
/// handle is live and exclusively borrowed; output slices are writable, aligned,
/// disjoint, and valid for their capacities; count/used point to writable usize values.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn lfc_next(
    handle: *mut Scanner,
    records: *mut Record,
    capacity: usize,
    names: *mut u8,
    name_capacity: usize,
    count: *mut usize,
    used: *mut usize,
) -> i32 {
    boundary(|| {
        if count.is_null() || used.is_null() {
            return Err(22);
        }
        // SAFETY: valid output counters are required by the caller contract.
        unsafe {
            *count = 0;
            *used = 0;
        }
        if handle.is_null()
            || records.is_null()
            || names.is_null()
            || capacity == 0
            || name_capacity == 0
            || capacity > isize::MAX as usize / size_of::<Record>()
            || name_capacity > u32::MAX as usize
        {
            return Err(22);
        }
        // SAFETY: the caller supplies exclusive, disjoint live allocations of these sizes.
        let (scanner, records, names) = unsafe {
            (
                &mut *handle,
                std::slice::from_raw_parts_mut(records, capacity),
                std::slice::from_raw_parts_mut(names, name_capacity),
            )
        };
        let (record_count, name_used) = scanner.next(records, names)?;
        // SAFETY: output counters remain live for the duration of the call.
        unsafe {
            *count = record_count;
            *used = name_used;
        }
        Ok(())
    })
}

/// # Safety
/// handle must be a live pointer returned by lfc_open, closed exactly once, without concurrent calls.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn lfc_close(handle: *mut Scanner) -> i32 {
    boundary(|| {
        if handle.is_null() {
            return Err(22);
        }
        // SAFETY: take back the unique Box ownership transferred by lfc_open.
        drop(unsafe { Box::from_raw(handle) });
        Ok(())
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn lfc_record_size() -> usize {
    size_of::<Record>()
}
