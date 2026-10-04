// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Files.Platform.Linux.Trash
{
	/// <summary>
	/// The result of an <c>lstat</c> (no symlink following) of a file system entry.
	/// </summary>
	/// <param name="IsDirectory">Whether the entry itself is a directory.</param>
	/// <param name="IsSymbolicLink">Whether the entry itself is a symbolic link.</param>
	/// <param name="OwnerUserId">The numeric owner.</param>
	/// <param name="Mode">The permission bits, including sticky, setuid and setgid.</param>
	public sealed record FileEntryInfo(bool IsDirectory, bool IsSymbolicLink, uint OwnerUserId, UnixFileMode Mode);

	/// <summary>
	/// Queries ownership and type of file system entries without following symbolic links; abstracted so tests can fake owners.
	/// </summary>
	public interface IFileOwnershipInspector
	{
		/// <summary>
		/// Gets the <c>lstat</c> information of <paramref name="path"/>, or <see langword="false"/> when it cannot be determined.
		/// </summary>
		bool TryGetInfo(string path, out FileEntryInfo info);
	}

	/// <summary>
	/// Implements <see cref="IFileOwnershipInspector"/> with the Linux <c>statx</c> system call. <c>struct statx</c> has the same
	/// layout on every architecture (256 bytes), so linux-x64 and linux-arm64 share this code. Needs glibc 2.28+ or musl 1.1.24+.
	/// </summary>
	public sealed partial class StatxFileOwnershipInspector : IFileOwnershipInspector
	{
		private const int AtFdCwd = -100;
		private const int AtSymlinkNofollow = 0x100;
		private const uint StatxType = 0x1;
		private const uint StatxMode = 0x2;
		private const uint StatxUid = 0x8;
		private const int StatxSize = 256;

		private const int UidOffset = 20;
		private const int ModeOffset = 28;

		/// <inheritdoc/>
		public unsafe bool TryGetInfo(string path, out FileEntryInfo info)
		{
			info = null!;

			try
			{
				var buffer = new byte[StatxSize];
				fixed (byte* p = buffer)
				{
					if (statx(AtFdCwd, path, AtSymlinkNofollow, StatxType | StatxMode | StatxUid, p) != 0)
						return false;
				}

				var mask = BitConverter.ToUInt32(buffer, 0);
				if ((mask & (StatxType | StatxMode | StatxUid)) != (StatxType | StatxMode | StatxUid))
					return false;

				var uid = BitConverter.ToUInt32(buffer, UidOffset);
				var mode = BitConverter.ToUInt16(buffer, ModeOffset);
				var type = mode & 0xF000;

				info = new FileEntryInfo(type == 0x4000, type == 0xA000, uid, (UnixFileMode)(mode & 0xFFF));
				return true;
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				return false;
			}
		}

		[LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
		private static unsafe partial int statx(int dirfd, string pathname, int flags, uint mask, byte* statxbuf);
	}
}
