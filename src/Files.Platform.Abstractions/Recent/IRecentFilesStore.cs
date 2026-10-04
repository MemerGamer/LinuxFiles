// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Files.Platform.Abstractions.Recent
{
	/// <summary>
	/// An entry of the recently used list.
	/// </summary>
	/// <param name="Path">The absolute path.</param>
	/// <param name="LastUsedUtc">When the item was last used.</param>
	/// <param name="MimeType">The MIME type, if recorded.</param>
	public sealed record RecentEntry(string Path, DateTime LastUsedUtc, string? MimeType);

	/// <summary>
	/// The user's recently used list, shared with other applications (<c>recently-used.xbel</c>).
	/// </summary>
	public interface IRecentFilesStore
	{
		/// <summary>
		/// Reads the entries, most recent first.
		/// </summary>
		IReadOnlyList<RecentEntry> Read();

		/// <summary>
		/// Adds (or refreshes) an entry for a path.
		/// </summary>
		bool Add(string path, string? mimeType = null);

		/// <summary>
		/// Removes the entry for a path.
		/// </summary>
		bool Remove(string path);

		/// <summary>
		/// Removes all entries.
		/// </summary>
		bool Clear();

		/// <summary>
		/// Gets raised when the list changed on disk (by this or another application).
		/// </summary>
		event EventHandler? Changed;
	}
}
