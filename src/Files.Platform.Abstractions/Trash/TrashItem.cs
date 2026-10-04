// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;

namespace Files.Platform.Abstractions.Trash
{
	/// <summary>
	/// Describes an item that currently lives in a trash folder.
	/// </summary>
	/// <param name="TrashedPath">The absolute path of the item inside the trash.</param>
	/// <param name="OriginalPath">The absolute path the item had before it was trashed.</param>
	/// <param name="DeletionDate">The time the item was trashed.</param>
	/// <param name="Size">The size in bytes; for directories the total size of their contents.</param>
	/// <param name="IsDirectory">Whether the item is a directory.</param>
	/// <param name="TrashId">An opaque identifier that is unique across all trash folders.</param>
	public sealed record TrashItem(
		string TrashedPath,
		string OriginalPath,
		DateTimeOffset DeletionDate,
		long Size,
		bool IsDirectory,
		string TrashId)
	{
		/// <summary>
		/// Gets the file or directory name the item had before it was trashed.
		/// </summary>
		public string Name => Path.GetFileName(OriginalPath.TrimEnd('/'));
	}
}
