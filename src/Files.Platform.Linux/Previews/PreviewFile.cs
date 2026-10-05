// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Native;
using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Threading;

namespace Files.Platform.Linux.Previews
{
	/// <summary>Opens only regular files, without blocking on FIFOs, including symbolic link targets.</summary>
	public static class PreviewFile
	{
		private const int ONoctty = 0x100;
		private const int OPath = 0x200000;
		private const int OCloexec = 0x80000;

		public static FileStream OpenRead(string path, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Opening a device can have side effects even with O_NONBLOCK. An O_PATH descriptor pins the (followed)
			// target without invoking any driver, so the type check below can't be raced by swapping the path.
			var pathFd = PosixNative.OpenAt(PosixNative.AtFdCwd, path, OPath | OCloexec, out var errno);
			if (pathFd < 0)
				throw PosixNative.CreateException(errno, path);

			using var pathHandle = new SafeFileHandle((IntPtr)pathFd, ownsHandle: true);
			if (!PosixNative.TryStat(pathFd, out var pinned) || !pinned.IsRegularFile)
				throw new IOException("Only regular files can be previewed.");

			// Reopening through the magic link reaches the pinned inode, not whatever the path names now.
			var fd = PosixNative.OpenAt(PosixNative.AtFdCwd, $"/proc/self/fd/{pathFd}",
				PosixNative.NonBlockingFlags | ONoctty, out errno);
			if (fd < 0)
				throw PosixNative.CreateException(errno, path);

			var handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
			try
			{
				if (!PosixNative.TryStat(fd, out var opened) || !opened.IsRegularFile || !PosixNative.SameEntry(pinned, opened))
					throw new IOException("Only regular files can be previewed.");
				PosixNative.ClearNonBlocking(fd);
				cancellationToken.ThrowIfCancellationRequested();
				return new FileStream(handle, FileAccess.Read);
			}
			catch
			{
				handle.Dispose();
				throw;
			}
		}
	}
}
