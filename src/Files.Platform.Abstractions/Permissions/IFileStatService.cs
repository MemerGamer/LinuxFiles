// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Permissions
{
	/// <summary>
	/// Low-level facts about a file system entry that the .NET file APIs do not expose.
	/// </summary>
	/// <param name="Size">The apparent size in bytes.</param>
	/// <param name="SizeOnDisk">The space allocated on disk in bytes, derived from the block count.</param>
	/// <param name="Created">The birth time, or null when the file system does not record it.</param>
	/// <param name="Modified">The last modification time.</param>
	/// <param name="Accessed">The last access time.</param>
	/// <param name="IsDirectory">Whether the entry is a directory.</param>
	/// <param name="IsSymbolicLink">Whether the entry is a symbolic link.</param>
	/// <param name="LinkTarget">The raw target of a symbolic link, otherwise null.</param>
	public sealed record FileStatInfo(
		long Size,
		long SizeOnDisk,
		DateTimeOffset? Created,
		DateTimeOffset Modified,
		DateTimeOffset Accessed,
		bool IsDirectory,
		bool IsSymbolicLink,
		string? LinkTarget);

	/// <summary>
	/// The totals of a folder scan.
	/// </summary>
	public readonly record struct FolderScanTotals(long Size, long SizeOnDisk, int Files, int Folders);

	/// <summary>
	/// Reads <c>stat</c>-level information.
	/// </summary>
	public interface IFileStatService
	{
		/// <summary>
		/// Gets the stat information of <paramref name="path"/> without following a final symbolic link.
		/// </summary>
		bool TryGetStat(string path, out FileStatInfo info);

		/// <summary>
		/// Walks a folder, skipping symbolic links, and reports running totals; cancellation returns the totals gathered so far.
		/// </summary>
		Task<FolderScanTotals> ScanFolderAsync(string path, IProgress<FolderScanTotals>? progress, CancellationToken cancellationToken);
	}
}
