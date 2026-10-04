// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Enumeration
{
	/// <summary>
	/// Describes a single file or folder produced by <see cref="IFileSystemEnumerator"/>.
	/// </summary>
	/// <param name="Name">The file name without the directory part.</param>
	/// <param name="FullPath">The absolute path of the entry.</param>
	/// <param name="IsDirectory">Whether the entry is a folder (for a followed symlink, whether its target is a folder).</param>
	/// <param name="IsSymlink">Whether the entry is a symbolic link.</param>
	/// <param name="IsBrokenSymlink">Whether the entry is a symbolic link whose target does not exist.</param>
	/// <param name="LinkTarget">The raw link target when <paramref name="IsSymlink"/> is set; otherwise <see langword="null"/>.</param>
	/// <param name="IsHidden">Whether the entry is hidden (a leading dot on Linux).</param>
	/// <param name="IsReadOnly">Whether the entry is not writable by the current user.</param>
	/// <param name="Length">The size in bytes; zero for folders.</param>
	/// <param name="CreationTimeUtc">The creation (birth) time in UTC.</param>
	/// <param name="LastWriteTimeUtc">The last modification time in UTC.</param>
	/// <param name="LastAccessTimeUtc">The last access time in UTC.</param>
	/// <param name="Attributes">The portable file attributes.</param>
	/// <param name="UnixMode">The unix permission bits when requested and available; otherwise <see langword="null"/>.</param>
	public sealed record FileSystemEntryInfo(
		string Name,
		string FullPath,
		bool IsDirectory,
		bool IsSymlink,
		bool IsBrokenSymlink,
		string? LinkTarget,
		bool IsHidden,
		bool IsReadOnly,
		long Length,
		DateTime CreationTimeUtc,
		DateTime LastWriteTimeUtc,
		DateTime LastAccessTimeUtc,
		FileAttributes Attributes,
		UnixFileMode? UnixMode);
}
