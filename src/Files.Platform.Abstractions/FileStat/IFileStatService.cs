// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.FileStat
{
	/// <summary>A stable file identity: the device and inode on Linux. Replaces the NTFS file reference number.</summary>
	public readonly record struct FileId(ulong Device, ulong Inode)
	{
		public override string ToString() => $"{Device:x}:{Inode:x}";

		/// <summary>Parses the <c>dev:ino</c> form produced by <see cref="ToString"/>.</summary>
		public static bool TryParse(string? text, out FileId id)
		{
			id = default;
			var parts = text?.Split(':');
			if (parts is not { Length: 2 }
				|| !ulong.TryParse(parts[0], System.Globalization.NumberStyles.AllowHexSpecifier, null, out var device)
				|| !ulong.TryParse(parts[1], System.Globalization.NumberStyles.AllowHexSpecifier, null, out var inode))
				return false;
			id = new FileId(device, inode);
			return true;
		}
	}

	/// <summary>Metadata of one path without following the final symbolic link unless requested.</summary>
	public readonly record struct FileStatInfo(bool IsDirectory, bool IsSymbolicLink, bool IsRegularFile, long Size, DateTime ModifiedUtc, FileId Id);

	/// <summary>Limits and callbacks for <see cref="IFileStatService.ScanFolderAsync"/>.</summary>
	public sealed record FolderScanOptions
	{
		public const int DefaultMaxDepth = 64;
		public const long DefaultMaxEntries = 20_000_000;

		/// <summary>Deepest folder level visited (the scanned folder is level 0).</summary>
		public int MaxDepth { get; init; } = DefaultMaxDepth;

		/// <summary>Upper bound on visited entries; the scan stops and reports <see cref="FolderScanResult.Truncated"/> beyond it.</summary>
		public long MaxEntries { get; init; } = DefaultMaxEntries;

		/// <summary>Called when a folder at <c>depth</c> has been fully summed (path, total bytes beneath it, depth).</summary>
		public Action<string, long, int>? FolderCompleted { get; init; }

		/// <summary>Called for each regular file counted (full path, size in bytes).</summary>
		public Action<string, long>? FileVisited { get; init; }

		/// <summary>Called with the running total of bytes as the scan progresses.</summary>
		public Action<long>? Progress { get; init; }
	}

	public readonly record struct FolderScanResult(long TotalSize, long FileCount, long FolderCount, bool Truncated, bool Canceled);

	/// <summary>Cheap stat and recursive size queries that never follow symbolic links while descending.</summary>
	public interface IFileStatService
	{
		/// <summary>
		/// Sums the regular files beneath <paramref name="folderPath"/>. Descends handle-relative, never follows symbolic links below the
		/// root, and is bounded by <paramref name="options"/>. Unreadable folders are skipped.
		/// </summary>
		Task<FolderScanResult> ScanFolderAsync(string folderPath, FolderScanOptions? options = null, CancellationToken cancellationToken = default);

		bool TryGetStat(string path, bool followSymlinks, out FileStatInfo stat);

		/// <summary>Reads the identity of <paramref name="path"/> (symbolic links are not followed).</summary>
		bool TryGetFileId(string path, out FileId id);
	}
}
