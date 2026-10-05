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

		public static FileStream OpenRead(string path, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			// Check the (followed) target before opening: opening a device can have side effects even with O_NONBLOCK.
			if (!PosixNative.TryStat(PosixNative.AtFdCwd, path, 0, out var expected, out var errno))
				throw PosixNative.CreateException(errno, path);
			if (!expected.IsRegularFile)
				throw new IOException("Only regular files can be previewed.");

			var fd = PosixNative.OpenAt(PosixNative.AtFdCwd, path,
				PosixNative.NonBlockingFlags | ONoctty, out errno);
			if (fd < 0)
				throw PosixNative.CreateException(errno, path);

			var handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
			try
			{
				// Reject a target swapped between the check and the open.
				if (!PosixNative.TryStat(fd, out var opened) || !opened.IsRegularFile || !PosixNative.SameEntry(expected, opened))
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
