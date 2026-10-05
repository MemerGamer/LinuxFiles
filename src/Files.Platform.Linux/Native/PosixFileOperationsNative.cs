// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using Files.Platform.Linux.FileOperations;
using Microsoft.Win32.SafeHandles;

namespace Files.Platform.Linux.Native
{
	internal static unsafe partial class PosixNative
	{
		private static readonly ConcurrentDictionary<(uint Major, uint Minor), byte> s_noRename2 = new();

		public static bool IsCrossDevice(int errno) => errno == 18;

		public static bool SameEntry(PosixStat first, PosixStat second)
			=> first.Inode == second.Inode && first.DevMajor == second.DevMajor && first.DevMinor == second.DevMinor && first.FileType == second.FileType;

		public static PosixStat StatAt(int dirfd, string name, string displayPath)
		{
			if (!TryStat(dirfd, name, AtSymlinkNofollow, out var stat, out var errno))
				throw CreateException(errno, displayPath);
			return stat;
		}

		public static bool TryRenameAt(int sourceFd, string source, int destinationFd, string destination, bool replace, out int errno, LinuxFileOperationsHooks? hooks = null)
		{
			var cache = hooks?.UnsupportedRenameDevices ?? s_noRename2;
			if (!TryStat(sourceFd, string.Empty, AtEmptyPath, out var parent, out errno))
				return false;
			var device = (parent.DevMajor, parent.DevMinor);
			if (replace || !cache.ContainsKey(device))
			{
				var injected = hooks?.RenameError?.Invoke(source, destination, replace);
				if (injected is { } error)
					errno = error;
				else
				{
					try
					{
						var result = replace ? renameat(sourceFd, source, destinationFd, destination)
							: renameat2(sourceFd, source, destinationFd, destination, 1);
						errno = result == 0 ? 0 : Marshal.GetLastPInvokeError();
					}
					catch (EntryPointNotFoundException) when (!replace)
					{
						errno = 38; // ENOSYS
					}
				}
				if (errno == 0)
					return true;
				if (replace || errno is not (22 or 38)) // EINVAL / ENOSYS
					return false;
				cache.TryAdd(device, 0);
			}

			if (!TryStat(sourceFd, source, AtSymlinkNofollow, out var stat, out errno))
				return false;
			if (stat.IsDirectory)
			{
				if (TryStat(destinationFd, destination, AtSymlinkNofollow, out _, out errno))
				{
					errno = 17; // EEXIST, including dangling symlinks
					return false;
				}
				if (!IsNotFound(errno))
					return false;
				// Filesystems without RENAME_NOREPLACE leave a small check/rename race for directories.
				var result = renameat(sourceFd, source, destinationFd, destination);
				errno = result == 0 ? 0 : Marshal.GetLastPInvokeError();
				return result == 0;
			}

			// With flags=0 linkat links the entry itself, including symlinks, and atomically rejects EEXIST.
			if (linkat(sourceFd, source, destinationFd, destination, 0) != 0)
			{
				errno = Marshal.GetLastPInvokeError();
				return false;
			}
			var removed = unlinkat(sourceFd, source, 0);
			errno = removed == 0 ? 0 : Marshal.GetLastPInvokeError();
			return removed == 0;
		}

		public static void RenameAt(int sourceFd, string source, int destinationFd, string destination, bool replace, string displayPath, LinuxFileOperationsHooks? hooks = null)
		{
			if (!TryRenameAt(sourceFd, source, destinationFd, destination, replace, out var errno, hooks))
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
			ChangeModeOfDescriptor(fd, false, stat.Mode & (stat.IsDirectory ? 0x3FFu : 0x1FFu), displayPath);
			long* times = stackalloc long[4] { 0, 0x3FFFFFFE, stat.ModifiedSeconds, stat.ModifiedNanoseconds }; // UTIME_OMIT for atime
			if (futimens(fd, times) != 0)
				throw CreateException(Marshal.GetLastPInvokeError(), displayPath);
		}

		[LibraryImport("libc", EntryPoint = "linkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
		private static partial int linkat(int sourceFd, string source, int destinationFd, string destination, int flags);

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
