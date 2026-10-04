// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Abstractions.FileOperations
{
	/// <summary>
	/// A snapshot of the progress of a file operation. Totals can grow if the tree changes while it is processed.
	/// </summary>
	/// <param name="ItemsProcessed">Files, links and folders handled so far.</param>
	/// <param name="ItemsTotal">Files, links and folders to handle.</param>
	/// <param name="BytesProcessed">File content bytes handled so far.</param>
	/// <param name="BytesTotal">File content bytes to handle.</param>
	/// <param name="CurrentItem">The path currently being processed.</param>
	public readonly record struct FileOperationProgress(
		long ItemsProcessed,
		long ItemsTotal,
		long BytesProcessed,
		long BytesTotal,
		string? CurrentItem);
}
