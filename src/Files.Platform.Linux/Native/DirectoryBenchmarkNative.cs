// Copyright (c) Files Community
// Licensed under the MIT License.

#if LINUX_DIRECTORY_BENCHMARK
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Files.Platform.Linux.Native
{
	// Compiled only by tools/Files.DirectoryBenchmarks, never by the app or elevation helper.
	internal static unsafe partial class DirectoryBenchmarkNative
	{
		internal const int BufferSize = 65536;
		internal const uint StatMask = 0x134B;
		internal readonly record struct Entry(string Name, PosixStat Stat);

		[StructLayout(LayoutKind.Sequential)]
		internal struct Record
		{
			public ulong Size, Inode;
			public long ModifiedSeconds;
			public ulong MountId;
			public uint Mode, OwnerId, ModifiedNanoseconds, DevMajor, DevMinor, Mask, NameOffset, NameLength;

			public readonly PosixStat Stat => new(Mode, Size, OwnerId, ModifiedSeconds, ModifiedNanoseconds,
				Inode, DevMajor, DevMinor, (Mask & 0x1000) != 0 ? MountId : null);
		}

		internal static List<Entry> Current(string path)
		{
			var fd = Open(path);
			try
			{
				var entries = new List<Entry>();
				PosixNative.ForEachName(fd, path, name =>
				{
					if (!PosixNative.TryStat(fd, name, PosixNative.AtSymlinkNofollow, out var stat, out var errno))
						throw PosixNative.CreateException(errno, path);
					entries.Add(new(name, stat));
					return true;
				});
				return entries;
			}
			finally { PosixNative.Close(fd); }
		}

		internal static List<Entry> Optimized(string path)
		{
			var fd = Open(path);
			try
			{
				var entries = new List<Entry>();
				var buffer = new byte[BufferSize];
				Span<byte> stat = stackalloc byte[256];
				fixed (byte* p = buffer)
				fixed (byte* s = stat)
				{
					while (true)
					{
						var length = GetDents(fd, p, (nuint)buffer.Length);
						if (length < 0)
						{
							var errno = Marshal.GetLastPInvokeError();
							if (errno == 4) continue;
							throw PosixNative.CreateException(errno, path);
						}
						if (length == 0) return entries;
						if (length > buffer.Length) throw new IOException("Invalid directory batch length.");
						for (var offset = 0; offset < length;)
						{
							var (recordLength, nameLength) = ValidateDirent(buffer.AsSpan(offset, (int)length - offset));
							var name = buffer.AsSpan(offset + 19, nameLength);
							if (!name.SequenceEqual("."u8) && !name.SequenceEqual(".."u8))
							{
								stat.Clear();
								int status;
								do { status = Statx(fd, p + offset + 19, PosixNative.AtSymlinkNofollow, StatMask, s); }
								while (status != 0 && Marshal.GetLastPInvokeError() == 4);
								if (status != 0) throw PosixNative.CreateException(Marshal.GetLastPInvokeError(), path);
								entries.Add(new(Encoding.UTF8.GetString(name), ParseStat(stat)));
							}
							offset += recordLength;
						}
					}
				}
			}
			finally { PosixNative.Close(fd); }
		}

		internal static (int RecordLength, int NameLength) ValidateDirent(ReadOnlySpan<byte> bytes)
		{
			if (bytes.Length < 24) throw new IOException("Truncated directory record.");
			var length = BinaryPrimitives.ReadUInt16LittleEndian(bytes[16..]);
			if (length < 24 || length > bytes.Length || (length & 7) != 0) throw new IOException("Invalid directory record length.");
			var nameLength = bytes.Slice(19, length - 19).IndexOf((byte)0);
			if (nameLength < 1 || bytes.Slice(19, nameLength).Contains((byte)'/')) throw new IOException("Invalid directory name.");
			return (length, nameLength);
		}

		private static PosixStat ParseStat(ReadOnlySpan<byte> bytes)
		{
			var mask = BitConverter.ToUInt32(bytes);
			if ((mask & 0xB) != 0xB) throw new IOException("Required stat fields unavailable.");
			return new(BitConverter.ToUInt16(bytes[28..]), (mask & 0x200) != 0 ? BitConverter.ToUInt64(bytes[40..]) : 0,
				BitConverter.ToUInt32(bytes[20..]), (mask & 0x40) != 0 ? BitConverter.ToInt64(bytes[112..]) : 0,
				(mask & 0x40) != 0 ? BitConverter.ToUInt32(bytes[120..]) : 0,
				(mask & 0x100) != 0 ? BitConverter.ToUInt64(bytes[32..]) : 0,
				BitConverter.ToUInt32(bytes[136..]), BitConverter.ToUInt32(bytes[140..]),
				(mask & 0x1000) != 0 ? BitConverter.ToUInt64(bytes[144..]) : null);
		}

		internal static List<Entry> Rust(string path)
		{
			Check(RustOpen(path, out var handle));
			try
			{
				var entries = new List<Entry>();
				var records = new Record[1024];
				var names = new byte[BufferSize];
				fixed (Record* r = records)
				fixed (byte* n = names)
				{
					while (true)
					{
						Check(RustNext(handle, r, (nuint)records.Length, n, (nuint)names.Length, out var count, out var used));
						if (count > (nuint)records.Length || used > (nuint)names.Length) throw new IOException("Invalid Rust batch.");
						if (count == 0) return entries;
						for (var i = 0; i < (int)count; i++)
						{
							var record = records[i];
							if ((ulong)record.NameOffset + record.NameLength > used || record.NameLength == 0 || (record.Mask & 0xB) != 0xB)
								throw new IOException("Invalid Rust record.");
							var name = names.AsSpan((int)record.NameOffset, (int)record.NameLength);
							if (name.Contains((byte)0) || name.Contains((byte)'/')) throw new IOException("Invalid Rust name.");
							entries.Add(new(Encoding.UTF8.GetString(name), record.Stat));
						}
					}
				}
			}
			finally { Check(RustClose(handle)); }
		}

		internal static void VerifyLayout()
		{
			Record record = default;
			var nameOffset = (byte*)&record.NameOffset - (byte*)&record;
			if (sizeof(Record) != 64 || RustRecordSize() != 64 || nameOffset != 56)
				throw new InvalidOperationException("Directory record ABI mismatch.");
		}

		private static int Open(string path)
		{
			var fd = PosixNative.OpenAt(PosixNative.AtFdCwd, path,
				PosixNative.ReadOnlyFlags | PosixNative.ODirectory | PosixNative.ONofollow, out var errno);
			return fd < 0 ? throw PosixNative.CreateException(errno, path) : fd;
		}

		private static void Check(int status)
		{
			if (status != 0) throw PosixNative.CreateException(status, "Rust directory scanner");
		}

		[LibraryImport("libc", EntryPoint = "getdents64", SetLastError = true)]
		private static partial nint GetDents(int fd, byte* buffer, nuint length);
		[LibraryImport("libc", EntryPoint = "statx", SetLastError = true)]
		private static partial int Statx(int fd, byte* name, int flags, uint mask, byte* stat);
		[LibraryImport("linuxfiles_core", EntryPoint = "lfc_open", StringMarshalling = StringMarshalling.Utf8)]
		private static partial int RustOpen(string path, out nint handle);
		[LibraryImport("linuxfiles_core", EntryPoint = "lfc_next")]
		private static partial int RustNext(nint handle, Record* records, nuint capacity, byte* names, nuint nameCapacity, out nuint count, out nuint used);
		[LibraryImport("linuxfiles_core", EntryPoint = "lfc_close")]
		private static partial int RustClose(nint handle);
		[LibraryImport("linuxfiles_core", EntryPoint = "lfc_record_size")]
		private static partial nuint RustRecordSize();
	}
}
#endif
