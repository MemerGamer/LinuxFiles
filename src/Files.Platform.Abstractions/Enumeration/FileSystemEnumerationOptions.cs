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
	/// Options controlling <see cref="IFileSystemEnumerator"/>.
	/// </summary>
	public sealed record FileSystemEnumerationOptions
	{
		/// <summary>
		/// The default number of entries read per batch.
		/// </summary>
		public const int DefaultBatchSize = 256;

		/// <summary>
		/// Gets whether hidden entries are returned.
		/// </summary>
		public bool IncludeHidden { get; init; }

		/// <summary>
		/// Gets whether symbolic links are resolved. When <see langword="false"/>, a link to a folder is reported
		/// as a non-directory link and recursion never enters it.
		/// </summary>
		public bool FollowSymlinks { get; init; } = true;

		/// <summary>
		/// Gets whether sub-folders are enumerated as well.
		/// </summary>
		public bool Recursive { get; init; }

		/// <summary>
		/// Gets whether <see cref="FileSystemEntryInfo.UnixMode"/> is populated (costs an extra stat per entry).
		/// </summary>
		public bool IncludeUnixMode { get; init; }

		/// <summary>
		/// Gets the number of entries read from disk per hop to the thread pool and per batch yielded.
		/// </summary>
		public int BatchSize { get; init; } = DefaultBatchSize;
	}
}
