// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Tags;
using Files.Platform.Linux.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Files.Platform.Linux.Tags
{
	/// <summary>
	/// Stores tags in the <c>user.xdg.tags</c> extended attribute (comma-separated UTF-8, the freedesktop convention shared with Dolphin and Nautilus extensions).
	/// File systems without user xattr support degrade to "no tags": reads return nothing, writes return false.
	/// </summary>
	public sealed class XattrFileTagsStore : IFileTagsStore
	{
		/// <summary>The attribute name.</summary>
		public const string AttributeName = "user.xdg.tags";

		/// <inheritdoc/>
		public IReadOnlyList<string> ReadTags(string path)
		{
			if (string.IsNullOrEmpty(path))
				return [];

			var value = XattrNative.Get(path, AttributeName, out _);
			if (value is null || value.Length == 0)
				return [];

			return Encoding.UTF8.GetString(value)
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Distinct(StringComparer.Ordinal)
				.ToArray();
		}

		/// <inheritdoc/>
		public bool WriteTags(string path, IReadOnlyList<string> tags)
		{
			if (string.IsNullOrEmpty(path))
				return false;

			// A comma would split one tag into two when read back
			var cleaned = tags.Select(t => t.Replace(',', ' ').Trim()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToArray();

			if (cleaned.Length == 0)
				return XattrNative.Remove(path, AttributeName, out _);

			return XattrNative.Set(path, AttributeName, Encoding.UTF8.GetBytes(string.Join(',', cleaned)), out _);
		}
	}
}
