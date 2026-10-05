// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Files.Platform.Linux.Native
{
	internal static unsafe partial class PosixNative
	{
		public static bool IsCrossDevice(int errno) => errno == 18;

		public static bool SameEntry(PosixStat first, PosixStat second)
			=> first.Inode == second.Inode && first.DevMajor == second.DevMajor && first.DevMinor == second.DevMinor && first.FileType == second.FileType;

		/// <summary>Reads struct stat without following the entry; layouts cover linux-x64 and linux-arm64.</summary>
		public static PosixStat StatAt(int dirfd, string name, string displayPath)
		{
			var buffer = new byte[144];
			fixed (byte* p = buffer)
			{
				if (fstatat(dirfd, name, p, AtSymlinkNofollow) != 0)
					throw CreateException(Marshal.GetLastPInvokeError(), displayPath);
			}

			var device = BitConverter.ToUInt64(buffer, 0);
			var major = (uint)(((device >> 8) & 0xFFF) | ((device >> 32) & 0xFFFFF000));
			var minor = (uint)((device & 0xFF) | ((device >> 12) & 0xFFFFFF00));
			return new PosixStat(BitConverter.ToUInt32(buffer, s_armLayout ? 16 : 24), BitConverter.ToUInt64(buffer, 48),
				BitConverter.ToUInt32(buffer, s_armLayout ? 24 : 28), BitConverter.ToInt64(buffer, 88), (uint)BitConverter.ToInt64(buffer, 96),
				BitConverter.ToUInt64(buffer, 8), major, minor);
		}

		public static bool TryRenameAt(int sourceFd, string source, int destinationFd, string destination, bool replace, out int errno)
		{
			// Never emulate NOREPLACE with a check followed by rename: another entry could appear in between.
			var result = replace ? renameat(sourceFd, source, destinationFd, destination)
				: renameat2(sourceFd, source, destinationFd, destination, 1);
			errno = result == 0 ? 0 : Marshal.GetLastPInvokeError();
			return result == 0;
		}

		public static void RenameAt(int sourceFd, string source, int destinationFd, string destination, bool replace, string displayPath)
		{
			if (!TryRenameAt(sourceFd, source, destinationFd, destination, replace, out var errno))
				throw CreateException(errno, displayPath);
		}

		public static void MkdirAt(int dirfd, string name, uint mode, string displayPath)
		{
			if (mkdirat(dirfd, name, mode) != 0)
				throw CreateException(Marshal.GetLastPInvokeError(), displayPath);
		}

		public static void SymlinkAt(string target, int dirfd, string name, string displayPath)
		{
			if (symlinkat(target, dirfd, name) != 0)
				throw CreateException(Marshal.GetLastPInvokeError(), displayPath);
		}

		public static FileStream OpenFileAt(int dirfd, string name, string displayPath, bool create)
		{
			var flags = NonBlockingFlags | ONofollow | (create ? 1 | 0x40 | 0x80 : 0); // O_WRONLY | O_CREAT | O_EXCL
			var fd = openat(dirfd, name, flags, 0x180); // 0600
			if (fd < 0)
				throw CreateException(Marshal.GetLastPInvokeError(), displayPath);
			var handle = new SafeFileHandle(fd, true);
			try
			{
				if (!TryStat(fd, out var stat) || !stat.IsRegularFile)
					throw new IOException($"'{displayPath}' is not a regular file.");
				return new FileStream(handle, create ? FileAccess.Write : FileAccess.Read, 1, false);
			}
			catch
			{
				handle.Dispose();
				throw;
			}
		}

		public static void SetMetadata(int fd, PosixStat stat, string displayPath)
		{
			ChangeModeOfDescriptor(fd, false, stat.Mode & 0xFFF, displayPath);
			long* times = stackalloc long[4] { 0, 0x3FFFFFFE, stat.ModifiedSeconds, stat.ModifiedNanoseconds }; // UTIME_OMIT for atime
			if (futimens(fd, times) != 0)
				throw CreateException(Marshal.GetLastPInvokeError(), displayPath);
		}

		[LibraryImport("libc", EntryPoint = "fstatat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
		private static partial int fstatat(int dirfd, string path, byte* stat, int flags);

		[LibraryImport("libc", EntryPoint = "renameat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
		private static partial int renameat(int sourceFd, string source, int destinationFd, string destination);

		[LibraryImport("libc", EntryPoint = "renameat2", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
		private static partial int renameat2(int sourceFd, string source, int destinationFd, string destination, uint flags);

		[LibraryImport("libc", EntryPoint = "mkdirat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
		private static partial int mkdirat(int dirfd, string name, uint mode);

		[LibraryImport("libc", EntryPoint = "symlinkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
		private static partial int symlinkat(string target, int dirfd, string name);

		[LibraryImport("libc", EntryPoint = "futimens", SetLastError = true)]
		private static partial int futimens(int fd, long* times);
	}
}
