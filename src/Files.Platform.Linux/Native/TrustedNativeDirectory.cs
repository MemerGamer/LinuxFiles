// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.Versioning;

namespace Files.Platform.Linux.Native
{
	/// <summary>
	/// Prepares a per-user directory holding a symbolic link to a native library that is about to be loaded, without any
	/// time-of-check/time-of-use gap on path names: the directory is reached by walking from <c>/</c> with one <c>openat</c> per component
	/// (<c>O_NOFOLLOW|O_DIRECTORY</c>), every opened descriptor is checked with <c>fstat</c> (owned by root or the current user, not
	/// writable by group or others), missing directories are created 0700, and the link is created and loaded through the descriptor of the
	/// final directory (<c>/proc/self/fd/N</c>), so replacing a path component afterwards cannot redirect the load.
	/// The link target must be a library in a root-owned, non-writable location.
	/// </summary>
	[SupportedOSPlatform("linux")]
	public static class TrustedNativeDirectory
	{
		private const uint GroupOtherWrite = 0x12;

		/// <summary>
		/// Creates <paramref name="cacheRoot"/>/<paramref name="subPath"/> and links <paramref name="linkName"/> to <paramref name="source"/>.
		/// Returns <c>/proc/self/fd/N</c> of the directory (the descriptor stays open for the life of the process), or <see langword="null"/>
		/// when any part of the chain is not trustworthy.
		/// </summary>
		/// <param name="cacheRoot">An absolute path (for example the XDG cache home).</param>
		/// <param name="subPath">Relative directories below the cache root.</param>
		/// <param name="linkName">The file name of the link.</param>
		/// <param name="source">The absolute library path the link points at.</param>
		/// <param name="userId">The user that must own the directories; the process user by default.</param>
		public static string? Prepare(string cacheRoot, string[] subPath, string linkName, string source, uint? userId = null)
		{
			var uid = userId ?? ProcessIdentityNative.CurrentUserId;
			if (!Path.IsPathRooted(cacheRoot) || !Path.IsPathRooted(source) || linkName.Contains('/') || linkName is "" or "." or "..")
				return null;

			if (!IsRootOwnedLibrary(source))
				return null;

			var parts = new System.Collections.Generic.List<string>();
			foreach (var part in cacheRoot.Split('/', StringSplitOptions.RemoveEmptyEntries))
				parts.Add(part);
			foreach (var part in subPath)
				parts.Add(part);

			if (parts.Exists(p => p is "." or ".." || p.Contains('\0')))
				return null;

			var current = PosixNative.OpenAt(PosixNative.AtFdCwd, "/", PosixNative.ODirectory | PosixNative.ReadOnlyFlags, out _);
			if (current < 0)
				return null;

			var keep = false;
			try
			{
				if (!IsTrusted(current, uid, mustBeUser: false))
					return null;

				var firstSubPath = parts.Count - subPath.Length;
				for (var i = 0; i < parts.Count; i++)
				{
					var flags = PosixNative.ODirectory | PosixNative.ONofollow | PosixNative.ReadOnlyFlags;
					var next = PosixNative.OpenAt(current, parts[i], flags, out var errno);
					if (next < 0 && PosixNative.IsNotFound(errno) && i >= firstSubPath - 1)
					{
						// Missing directories are created 0700 (the cache root itself, then the app's subfolders)
						if (!PosixNative.MakeDirectoryAt(current, parts[i], out _))
							return null;
						next = PosixNative.OpenAt(current, parts[i], flags, out _);
					}

					PosixNative.Close(current);
					current = next;
					if (current < 0)
						return null;

					// Components the app created (below the cache root) must belong to the user; the ones above may be root's
					if (!IsTrusted(current, uid, mustBeUser: i >= firstSubPath))
						return null;
				}

				// Replace the link through the descriptor; a stale file or link of the same name is removed first
				try { PosixNative.UnlinkAt(current, linkName, 0, linkName); } catch (IOException) { } catch (UnauthorizedAccessException) { }
				if (!PosixNative.SymlinkAt(source, current, linkName, out _))
					return null;

				keep = true;
				return "/proc/self/fd/" + current;
			}
			finally
			{
				if (!keep && current >= 0)
					PosixNative.Close(current);
			}
		}

		private static bool IsTrusted(int fd, uint uid, bool mustBeUser)
		{
			if (!PosixNative.TryStat(fd, out var stat) || !stat.IsDirectory)
				return false;

			if (mustBeUser ? stat.OwnerUserId != uid : stat.OwnerUserId != 0 && stat.OwnerUserId != uid)
				return false;

			return (stat.Mode & GroupOtherWrite) == 0;
		}

		/// <summary>
		/// A regular file whose real path (symlinks resolved) and every directory above it belong to root and are not writable by others.
		/// Root-owned, non-writable parents mean only root could have planted any link on the way.
		/// </summary>
		private static bool IsRootOwnedLibrary(string path)
		{
			try
			{
				var real = File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? Path.GetFullPath(path);
				if (!PosixNative.TryStat(PosixNative.AtFdCwd, real, 0, out var file) || !file.IsRegularFile || file.OwnerUserId != 0 || (file.Mode & GroupOtherWrite) != 0)
					return false;

				for (var dir = Path.GetDirectoryName(real); dir is not null; dir = Path.GetDirectoryName(dir))
				{
					if (!PosixNative.TryStat(PosixNative.AtFdCwd, dir, 0, out var stat) || !stat.IsDirectory || stat.OwnerUserId != 0 || (stat.Mode & GroupOtherWrite) != 0)
						return false;
				}

				return true;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
			{
				return false;
			}
		}
	}
}
