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
	/// Streams the entries of a folder without blocking the caller.
	/// </summary>
	public interface IFileSystemEnumerator
	{
		/// <summary>
		/// Enumerates the entries of a folder one by one. Inaccessible entries are skipped.
		/// </summary>
		/// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
		IAsyncEnumerable<FileSystemEntryInfo> EnumerateAsync(string folderPath, FileSystemEnumerationOptions? options = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Enumerates the entries of a folder in batches of at most <see cref="FileSystemEnumerationOptions.BatchSize"/>.
		/// </summary>
		IAsyncEnumerable<IReadOnlyList<FileSystemEntryInfo>> EnumerateBatchesAsync(string folderPath, FileSystemEnumerationOptions? options = null, CancellationToken cancellationToken = default);
	}
}
