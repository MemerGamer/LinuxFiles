// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Enumeration;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Files.Platform.Abstractions.Search
{
	/// <summary>
	/// Options of a recursive file search.
	/// </summary>
	public sealed record FileSearchOptions
	{
		/// <summary>Gets whether hidden entries (dot files) are searched and returned.</summary>
		public bool IncludeHidden { get; init; }

		/// <summary>Gets the maximum folder depth below the root that is entered (0 searches the root only).</summary>
		public int MaxDepth { get; init; } = 32;

		/// <summary>Gets the maximum number of results; 0 means no limit.</summary>
		public int MaxResults { get; init; } = 10_000;

		/// <summary>Gets the maximum duration of the whole search; <see cref="TimeSpan.Zero"/> means no limit.</summary>
		public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

		/// <summary>Gets whether the content of small text files is searched instead of the names.</summary>
		public bool SearchContent { get; init; }

		/// <summary>Gets the largest file (in bytes) whose content is searched.</summary>
		public long MaxContentFileSize { get; init; } = 1024 * 1024;
	}

	/// <summary>
	/// A search hit.
	/// </summary>
	/// <param name="Entry">The matching file or folder.</param>
	/// <param name="ContentSnippet">The first matching line for a content search; otherwise <see langword="null"/>.</param>
	public sealed record FileSearchMatch(FileSystemEntryInfo Entry, string? ContentSnippet);

	/// <summary>
	/// Searches a folder tree by file name (case-insensitive substring or <c>*</c>/<c>?</c> wildcard) or by text content.
	/// </summary>
	public interface IFileSearchService
	{
		/// <summary>
		/// Streams the matches below <paramref name="rootPath"/>. Symbolic links to folders are not entered, inaccessible folders are skipped.
		/// Reaching <see cref="FileSearchOptions.Timeout"/> or <see cref="FileSearchOptions.MaxResults"/> ends the stream normally;
		/// cancelling through <paramref name="cancellationToken"/> throws <see cref="OperationCanceledException"/>.
		/// </summary>
		IAsyncEnumerable<FileSearchMatch> SearchAsync(string rootPath, string pattern, FileSearchOptions? options = null, CancellationToken cancellationToken = default);
	}
}
