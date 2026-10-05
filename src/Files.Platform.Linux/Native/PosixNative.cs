// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Files.Platform.Linux.Native
{
	/// <summary>
	/// The result of a <c>statx</c> call.
	/// </summary>
	internal readonly record struct PosixStat(uint Mode, ulong Size, uint OwnerUserId, long ModifiedSeconds, uint ModifiedNanoseconds, ulong Inode = 0, uint DevMajor = 0, uint DevMinor = 0)
	{
		public uint FileType => Mode & 0xF000;

		public bool IsDirectory => FileType == 0x4000;

		public bool IsSymbolicLink => FileType == 0xA000;

		public bool IsRegularFile => FileType == 0x8000;

		/// <summary>FIFOs, sockets, character and block devices: never opened for copying because that can block or have side effects.</summary>
		public bool IsSpecial => FileType is 0x1000 or 0x2000 or 0x6000 or 0xC000;

		public DateTime ModifiedUtc => DateTime.UnixEpoch.AddTicks(ModifiedSeconds * TimeSpan.TicksPerSecond + ModifiedNanoseconds / 100);
	}

	/// <summary>
	/// Minimal handle-relative libc calls (<c>openat</c>, <c>unlinkat</c>, <c>statx</c>, directory listing). Working relative to an open
	/// directory descriptor with <c>O_NOFOLLOW</c> means a path component swapped for a symbolic link cannot redirect an operation.
	/// <c>struct statx</c> and <c>struct dirent</c> (d_name at offset 19) have the same layout on linux-x64 and linux-arm64.
	/// </summary>
	internal static unsafe partial class PosixNative
	{
		public const int AtFdCwd = -100;
		public const int AtSymlinkNofollow = 0x100;
		public const int AtRemoveDir = 0x200;
		public const int AtEmptyPath = 0x1000;

		private const uint StatxType = 0x1;
		private const uint StatxMode = 0x2;
		private const uint StatxUid = 0x8;
		private const uint StatxMtime = 0x40;
		private const uint StatxIno = 0x100;
		private const uint StatxSize = 0x200;
		private const int StatxBufferSize = 256;

		public const int ONonblock = 0x800;
		private const int OCloexec = 0x80000;

		private const int ENOENT = 2;
		private const int ELOOP = 40;
		private const int ENOTDIR = 20;

		private static readonly bool s_armLayout = RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm;

		// O_DIRECTORY and O_NOFOLLOW differ between x86-64 and ARM
		public static int ODirectory => s_armLayout ? 0x4000 : 0x10000;

		public static int ONofollow => s_armLayout ? 0x8000 : 0x20000;

		public static int ReadOnlyFlags => OCloexec;

		public static int NonBlockingFlags => OCloexec | ONonblock;

		public static bool IsNotFollowedError(int errno) => errno is ELOOP or ENOTDIR;

		public static bool IsNotFound(int errno) => errno == ENOENT;

		/// <summary>Runs <c>statx</c>; returns <see langword="false"/> (with <paramref name="errno"/> set) on failure.</summary>
		public static bool TryStat(int dirfd, string path, int flags, out PosixStat stat)
			=> TryStat(dirfd, path, flags, out stat, out _);

		public static bool TryStat(int dirfd, string path, int flags, out PosixStat stat, out int errno)
		{
			stat = default;
			errno = 0;

			try
			{
				var buffer = new byte[StatxBufferSize];
				fixed (byte* p = buffer)
				{
					const uint mask = StatxType | StatxMode | StatxUid | StatxMtime | StatxSize | StatxIno;
					if (statx(dirfd, path, flags, mask, p) != 0)
					{
						errno = Marshal.GetLastPInvokeError();
						return false;
					}
				}

				var returned = BitConverter.ToUInt32(buffer, 0);
				const uint required = StatxType | StatxMode | StatxUid;
				if ((returned & required) != required)
					return false;

				var size = (returned & StatxSize) != 0 ? BitConverter.ToUInt64(buffer, 40) : 0;
				var seconds = (returned & StatxMtime) != 0 ? BitConverter.ToInt64(buffer, 112) : 0;
				var nanos = (returned & StatxMtime) != 0 ? BitConverter.ToUInt32(buffer, 120) : 0;

				stat = new PosixStat(BitConverter.ToUInt16(buffer, 28), size, BitConverter.ToUInt32(buffer, 20), seconds, nanos,
					(returned & StatxIno) != 0 ? BitConverter.ToUInt64(buffer, 32) : 0, BitConverter.ToUInt32(buffer, 136), BitConverter.ToUInt32(buffer, 140));
				return true;
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				return false;
			}
		}

		/// <summary>Stats an open descriptor.</summary>
		public static bool TryStat(int fd, out PosixStat stat)
			=> TryStat(fd, string.Empty, AtEmptyPath, out stat, out _);

		/// <summary>Opens <paramref name="name"/> relative to <paramref name="dirfd"/>; returns -1 with <paramref name="errno"/> set on failure.</summary>
		public static int OpenAt(int dirfd, string name, int flags, out int errno)
		{
			var fd = openat(dirfd, name, flags, 0);
			errno = fd < 0 ? Marshal.GetLastPInvokeError() : 0;
			return fd;
		}

		/// <summary>Checks the mode of an open descriptor using fstat; struct stat differs by architecture.</summary>
		public static bool IsRegularFile(int fd)
		{
			byte* buffer = stackalloc byte[256];
			if (fstat(fd, buffer) != 0)
				throw CreateException(Marshal.GetLastPInvokeError(), "preview descriptor");
			var modeOffset = RuntimeInformation.ProcessArchitecture switch
			{
				Architecture.X64 => 24,
				Architecture.Arm64 => 16,
				_ => throw new PlatformNotSupportedException("Preview fstat requires linux-x64 or linux-arm64."),
			};
			var mode = *(uint*)(buffer + modeOffset);
			return (mode & 0xF000) == 0x8000;
		}

		public static void ClearNonBlocking(int fd)
		{
			const int getFlags = 3, setFlags = 4;
			var flags = fcntl(fd, getFlags, 0);
			if (flags < 0 || fcntl(fd, setFlags, flags & ~ONonblock) < 0)
				throw CreateException(Marshal.GetLastPInvokeError(), "preview descriptor");
		}

		public static void Close(int fd) => _ = close(fd);

		/// <summary>Removes a file, link or (with <see cref="AtRemoveDir"/>) empty directory relative to <paramref name="dirfd"/>.</summary>
		public static void UnlinkAt(int dirfd, string name, int flags, string displayPath)
		{
			if (unlinkat(dirfd, name, flags) != 0)
				throw CreateException(Marshal.GetLastPInvokeError(), displayPath);
		}

		public static string ReadLinkAt(int dirfd, string name, string displayPath)
		{
			var buffer = new byte[4096];
			fixed (byte* p = buffer)
			{
				var length = readlinkat(dirfd, name, p, (nuint)buffer.Length);
				if (length < 0)
					throw CreateException(Marshal.GetLastPInvokeError(), displayPath);

				return Encoding.UTF8.GetString(buffer, 0, (int)length);
			}
		}

		/// <summary>Lists entry names of an open directory descriptor without touching its path.</summary>
		public static List<string> ListNames(int dirfd, string displayPath)
		{
			var duplicate = dup(dirfd);
			if (duplicate < 0)
				throw CreateException(Marshal.GetLastPInvokeError(), displayPath);

			var stream = fdopendir(duplicate);
			if (stream == 0)
			{
				var errno = Marshal.GetLastPInvokeError();
				close(duplicate);
				throw CreateException(errno, displayPath);
			}

			var names = new List<string>();
			try
			{
				while (true)
				{
					var entry = readdir(stream);
					if (entry is null)
						break;

					var name = Marshal.PtrToStringUTF8((nint)(entry + 19))!;
					if (name is not ("." or ".."))
						names.Add(name);
				}
			}
			finally
			{
				closedir(stream);
			}

			return names;
		}

		/// <summary>Maps an errno to the exception type the rest of the file operations code classifies.</summary>
		public static Exception CreateException(int errno, string path)
			=> errno switch
			{
				ENOENT => new FileNotFoundException("The item does not exist.", path),
				1 or 13 or 30 => new UnauthorizedAccessException($"Access to '{path}' is denied."),
				_ => new IOException($"The operation on '{path}' failed with errno {errno}.", errno),
			};

		[LibraryImport("libc", EntryPoint = "fstat", SetLastError = true)]
		private static partial int fstat(int fd, byte* buffer);

		[LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
		private static partial int fcntl(int fd, int command, int argument);

		[LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
		private static partial int statx(int dirfd, string pathname, int flags, uint mask, byte* statxbuf);

		[LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
		private static partial int openat(int dirfd, string pathname, int flags, uint mode);

		[LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
		private static partial int close(int fd);

		[LibraryImport("libc", EntryPoint = "dup", SetLastError = true)]
		private static partial int dup(int fd);

		[LibraryImport("libc", EntryPoint = "unlinkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
		private static partial int unlinkat(int dirfd, string pathname, int flags);

		[LibraryImport("libc", EntryPoint = "readlinkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
		private static partial nint readlinkat(int dirfd, string pathname, byte* buffer, nuint size);

		[LibraryImport("libc", EntryPoint = "fdopendir", SetLastError = true)]
		private static partial nint fdopendir(int fd);

		[LibraryImport("libc", EntryPoint = "readdir", SetLastError = true)]
		private static partial byte* readdir(nint stream);

		[LibraryImport("libc", EntryPoint = "closedir")]
		private static partial int closedir(nint stream);
	}

	/// <summary>
	/// An open directory descriptor that child operations are performed relative to.
	/// </summary>
	internal sealed class DirectoryHandle : IDisposable
	{
		private int _fd;

		private DirectoryHandle(int fd, string path)
		{
			_fd = fd;
			Path = path;
		}

		public string Path { get; }

		public int Descriptor => _fd;

		/// <summary>
		/// Opens a directory. With <paramref name="noFollow"/> a symbolic link in the last component is refused (errno ELOOP/ENOTDIR).
		/// </summary>
		public static DirectoryHandle? TryOpen(int parentFd, string name, string displayPath, bool noFollow, out int errno)
		{
			var flags = PosixNative.ReadOnlyFlags | PosixNative.ODirectory | (noFollow ? PosixNative.ONofollow : 0);
			var fd = PosixNative.OpenAt(parentFd, name, flags, out errno);
			return fd < 0 ? null : new DirectoryHandle(fd, displayPath);
		}

		public List<string> ListNames() => PosixNative.ListNames(_fd, Path);

		public void Dispose()
		{
			if (_fd >= 0)
			{
				PosixNative.Close(_fd);
				_fd = -1;
			}
		}
	}
}
