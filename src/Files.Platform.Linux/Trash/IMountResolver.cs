// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Files.Platform.Linux.Trash
{
	/// <summary>
	/// Describes a mounted file system.
	/// </summary>
	/// <param name="MountPoint">The absolute mount point (the "topdir" of the file system).</param>
	/// <param name="FileSystemType">The file system type, for example <c>ext4</c>.</param>
	public sealed record MountEntry(string MountPoint, string FileSystemType);

	/// <summary>
	/// Resolves mount points; abstracted so the topdir trash selection can be tested with a fake mount table.
	/// </summary>
	public interface IMountResolver
	{
		/// <summary>
		/// Gets all mounted file systems.
		/// </summary>
		IReadOnlyList<MountEntry> GetMounts();

		/// <summary>
		/// Gets the mount point that contains the already fully resolved absolute <paramref name="realPath"/>.
		/// </summary>
		string GetMountPoint(string realPath);
	}
}
