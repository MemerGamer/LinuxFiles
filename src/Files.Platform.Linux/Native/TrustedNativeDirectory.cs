// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;

namespace Files.Platform.Linux.Native
{
	/// <summary>
	/// Prepares a per-user directory holding a symbolic link to a native library that is about to be loaded. Anything another user could
	/// have replaced is rejected: every directory from the root down must belong to root or the current user and not be writable by
	/// group or others, the link directory is created 0700 and must not be a symlink, and the link target must be trusted as well.
	/// </summary>
	[System.Runtime.Versioning.SupportedOSPlatform("linux")]
	public static class TrustedNativeDirectory
	{
		private const uint GroupOtherWrite = 0x12;

		/// <summary>
		/// Creates <paramref name="cacheRoot"/>/<paramref name="subPath"/> and links <paramref name="linkName"/> to <paramref name="source"/>.
		/// Returns the directory, or <see langword="null"/> when any part of the chain is not trustworthy.
		/// </summary>
		/// <param name="cacheRoot">An absolute path (for example the XDG cache home).</param>
		/// <param name="subPath">Relative directories below the cache root.</param>
		/// <param name="linkName">The file name of the link.</param>
		/// <param name="source">The absolute library path the link points at.</param>
		/// <param name="userId">The user that must own the created directories; the process user by default.</param>
		public static string? Prepare(string cacheRoot, string[] subPath, string linkName, string source, uint? userId = null)
		{
			var uid = userId ?? ProcessIdentityNative.CurrentUserId;
			if (!Path.IsPathRooted(cacheRoot) || !Path.IsPathRooted(source) || linkName.Contains('/') || linkName is "" or "." or "..")
				return null;

			// The cache root and everything above it: owned by root or us, not writable by others (symlinks are resolved by the kernel)
			if (!IsTrustedAncestorChain(Path.GetFullPath(cacheRoot), uid))
				return null;

			if (!IsTrustedFile(source, uid))
				return null;

			var directory = Path.GetFullPath(cacheRoot);
			foreach (var part in subPath)
			{
				if (part is "" or "." or ".." || part.Contains('/'))
					return null;

				directory = Path.Combine(directory, part);
				if (!Directory.Exists(directory) && !PathExistsNoFollow(directory))
					Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

				if (!PosixNative.TryStat(PosixNative.AtFdCwd, directory, PosixNative.AtSymlinkNofollow, out var stat)
					|| !stat.IsDirectory || stat.OwnerUserId != uid || (stat.Mode & GroupOtherWrite) != 0)
					return null;
			}

			var link = Path.Combine(directory, linkName);
			try
			{
				var existing = new FileInfo(link);
				if (existing.LinkTarget != source)
				{
					if (existing.Exists || existing.LinkTarget is not null)
						existing.Delete();
					File.CreateSymbolicLink(link, source);
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return null;
			}

			return directory;
		}

		private static bool PathExistsNoFollow(string path)
			=> PosixNative.TryStat(PosixNative.AtFdCwd, path, PosixNative.AtSymlinkNofollow, out _);

		private static bool IsTrustedAncestorChain(string path, uint uid)
		{
			for (var current = path; current is not null; current = Path.GetDirectoryName(current))
			{
				if (!PosixNative.TryStat(PosixNative.AtFdCwd, current, 0, out var stat) || !stat.IsDirectory)
					return false;

				if ((stat.OwnerUserId != 0 && stat.OwnerUserId != uid) || (stat.Mode & GroupOtherWrite) != 0)
					return false;
			}

			return true;
		}

		private static bool IsTrustedFile(string path, uint uid)
		{
			if (!PosixNative.TryStat(PosixNative.AtFdCwd, path, 0, out var stat) || !stat.IsRegularFile)
				return false;

			if ((stat.OwnerUserId != 0 && stat.OwnerUserId != uid) || (stat.Mode & GroupOtherWrite) != 0)
				return false;

			var parent = Path.GetDirectoryName(path);
			return parent is not null && IsTrustedAncestorChain(parent, uid);
		}
	}
}
