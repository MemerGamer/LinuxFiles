// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Files.Platform.Abstractions.Tags
{
	/// <summary>
	/// Stores file tags alongside the file, in the file system (extended attributes on Linux).
	/// </summary>
	public interface IFileTagsStore
	{
		/// <summary>
		/// Reads the tags of a file. Returns an empty list if the file has none or the file system cannot store them.
		/// </summary>
		IReadOnlyList<string> ReadTags(string path);

		/// <summary>
		/// Replaces the tags of a file. An empty list removes the tags. Returns false if the tags could not be stored
		/// (unsupported file system, read-only, no permission); callers keep their own database as a fallback.
		/// </summary>
		bool WriteTags(string path, IReadOnlyList<string> tags);
	}
}
