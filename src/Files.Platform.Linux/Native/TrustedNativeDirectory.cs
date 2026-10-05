// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.Versioning;

namespace Files.Platform.Linux.Native
{
	/// <summary>
	/// Prepares a per-user directory holding a symbolic link to a native library that is about to be loaded, without any
	/// time-of-check/time-of-use gap on path names. Both the directory and the library are reached by walking from <c>/</c> with one
	/// <c>openat</c> per component (<c>O_NOFOLLOW</c>; symlinks on the way are resolved here, component by component), and every opened
	/// descriptor is checked with <c>fstat</c>: owned by root or the current user and not writable by group or others (so sticky
	/// world-writable directories such as <c>/tmp</c> are rejected). The library descriptor stays open and the cache link points at
	/// <c>/proc/self/fd/&lt;libfd&gt;</c>, which resolves to the pinned inode; the link is created and loaded through the descriptor of the cache
	/// directory (<c>/proc/self/fd/&lt;dirfd&gt;</c>). Swapping any path component afterwards cannot redirect the load.
	/// </summary>
	[SupportedOSPlatform("linux")]
	public static class TrustedNativeDirectory
	{
		private const uint GroupOtherWrite = 0x12;
		private const int MaxSymlinkHops = 16;

		/// <summary>
		/// Creates <paramref name="cacheRoot"/>/<paramref name="subPath"/> and links <paramref name="linkName"/> to <paramref name="source"/>.
		/// Returns <c>/proc/self/fd/N</c> of the directory (the descriptors stay open for the life of the process), or <see langword="null"/>
		/// when any part of either chain is not trustworthy.
		/// </summary>
		/// <param name="cacheRoot">An absolute path (for example the XDG cache home).</param>
		/// <param name="subPath">Relative directories below the cache root.</param>
		/// <param name="linkName">The file name of the link.</param>
		/// <param name="source">The absolute library path to load.</param>
		/// <param name="userId">The user that must own the directories; the process user by default.</param>
		public static string? Prepare(string cacheRoot, string[] subPath, string linkName, string source, uint? userId = null)
		{
			var uid = userId ?? ProcessIdentityNative.CurrentUserId;
			if (!Path.IsPathRooted(cacheRoot) || !Path.IsPathRooted(source) || linkName.Contains('/') || linkName is "" or "." or "..")
				return null;

			var parts = new System.Collections.Generic.List<string>();
			foreach (var part in cacheRoot.Split('/', StringSplitOptions.RemoveEmptyEntries))
				parts.Add(part);
			foreach (var part in subPath)
				parts.Add(part);

			if (parts.Exists(p => p is "." or ".." || p.Contains('\0')))
				return null;

			var libraryFd = OpenTrustedFile(source, uid);
			if (libraryFd < 0)
				return null;

			var current = PosixNative.OpenAt(PosixNative.AtFdCwd, "/", PosixNative.ODirectory | PosixNative.ReadOnlyFlags, out _);
			if (current < 0)
			{
				PosixNative.Close(libraryFd);
				return null;
			}

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
					if (next < 0 && PosixNative.IsNotFound(errno) && IsTrusted(current, uid, mustBeUser: true))
					{
						// Missing directories are created 0700, but only below a validated ancestor that belongs to the user
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

				// Replace the link through the directory descriptor; it points at the pinned library inode, not at a path name
				try { PosixNative.UnlinkAt(current, linkName, 0, linkName); } catch (IOException) { } catch (UnauthorizedAccessException) { }
				if (!PosixNative.SymlinkAt("/proc/self/fd/" + libraryFd, current, linkName, out _))
					return null;

				keep = true;
				return "/proc/self/fd/" + current;
			}
			finally
			{
				if (!keep)
				{
					PosixNative.Close(libraryFd);
					if (current >= 0)
						PosixNative.Close(current);
				}
			}
		}

		/// <summary>
		/// Opens a regular file by walking its path with validated descriptors, resolving symlinks component by component.
		/// Every directory on the way, and the file, must be owned by root or the user and not writable by group or others.
		/// </summary>
		private static int OpenTrustedFile(string path, uint uid)
		{
			var queue = new System.Collections.Generic.LinkedList<string>(path.Split('/', StringSplitOptions.RemoveEmptyEntries));
			var dir = PosixNative.OpenAt(PosixNative.AtFdCwd, "/", PosixNative.ODirectory | PosixNative.ReadOnlyFlags, out _);
			if (dir < 0)
				return -1;

			var hops = 0;
			try
			{
				if (!IsTrusted(dir, uid, mustBeUser: false))
					return -1;

				while (queue.First is { } node)
				{
					var name = node.Value;
					queue.RemoveFirst();
					if (name == ".")
						continue;

					if (name == ".." || name.Contains('\0'))
						return -1;

					if (!PosixNative.TryStat(dir, name, PosixNative.AtSymlinkNofollow, out var stat))
						return -1;

					if (stat.IsSymbolicLink)
					{
						// The link lives in a validated directory, so only root or the user could have planted it
						if (++hops > MaxSymlinkHops)
							return -1;

						var target = PosixNative.ReadLinkAt(dir, name, path);
						var targetParts = target.Split('/', StringSplitOptions.RemoveEmptyEntries);
						for (var i = targetParts.Length - 1; i >= 0; i--)
							queue.AddFirst(targetParts[i]);

						if (target.StartsWith('/'))
						{
							PosixNative.Close(dir);
							dir = PosixNative.OpenAt(PosixNative.AtFdCwd, "/", PosixNative.ODirectory | PosixNative.ReadOnlyFlags, out _);
							if (dir < 0)
								return -1;
						}

						continue;
					}

					if (queue.Count == 0)
					{
						if (!stat.IsRegularFile)
							return -1;

						var file = PosixNative.OpenAt(dir, name, PosixNative.ONofollow | PosixNative.ReadOnlyFlags, out _);
						if (file < 0)
							return -1;

						if (PosixNative.TryStat(file, out var opened) && opened.IsRegularFile && IsAcceptableOwner(opened.OwnerUserId, uid) && (opened.Mode & GroupOtherWrite) == 0)
							return file;

						PosixNative.Close(file);
						return -1;
					}

					var next = PosixNative.OpenAt(dir, name, PosixNative.ODirectory | PosixNative.ONofollow | PosixNative.ReadOnlyFlags, out _);
					PosixNative.Close(dir);
					dir = next;
					if (dir < 0 || !IsTrusted(dir, uid, mustBeUser: false))
						return -1;
				}

				return -1;
			}
			finally
			{
				if (dir >= 0)
					PosixNative.Close(dir);
			}
		}

		private static bool IsAcceptableOwner(uint owner, uint uid) => owner == 0 || owner == uid;

		private static bool IsTrusted(int fd, uint uid, bool mustBeUser)
		{
			if (!PosixNative.TryStat(fd, out var stat) || !stat.IsDirectory)
				return false;

			if (mustBeUser ? stat.OwnerUserId != uid : !IsAcceptableOwner(stat.OwnerUserId, uid))
				return false;

			return (stat.Mode & GroupOtherWrite) == 0;
		}
	}
}
