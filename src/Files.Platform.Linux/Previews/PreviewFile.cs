// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Native;
using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Threading;

namespace Files.Platform.Linux.Previews
{
	/// <summary>Opens only regular files, without blocking on FIFOs or following the final symlink.</summary>
	public static class PreviewFile
	{
		public static FileStream OpenRead(string path, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var fd = PosixNative.OpenAt(PosixNative.AtFdCwd, path,
				PosixNative.NonBlockingFlags | PosixNative.ONofollow, out var errno);
			if (fd < 0)
				throw PosixNative.CreateException(errno, path);

			var handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
			try
			{
				if (!PosixNative.IsRegularFile(fd))
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
