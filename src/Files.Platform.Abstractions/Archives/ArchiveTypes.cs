// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Files.Platform.Abstractions.FileOperations;

namespace Files.Platform.Abstractions.Archives
{
	/// <summary>
	/// Archive formats that can be created.
	/// </summary>
	public enum ArchiveFormat
	{
		/// <summary>A zip archive.</summary>
		Zip = 0,

		/// <summary>An uncompressed tar archive.</summary>
		Tar,

		/// <summary>A gzip compressed tar archive (.tar.gz).</summary>
		TarGz,

		/// <summary>A bzip2 compressed tar archive (.tar.bz2).</summary>
		TarBz2,

		/// <summary>A 7z archive. Only available when an external 7-Zip binary is installed.</summary>
		SevenZip,
	}

	/// <summary>
	/// The compression effort used when creating an archive.
	/// </summary>
	public enum ArchiveCompressionLevel
	{
		/// <summary>Store only.</summary>
		None = 0,

		/// <summary>Fastest compression.</summary>
		Fast,

		/// <summary>Balanced.</summary>
		Normal,

		/// <summary>Better compression, slower.</summary>
		High,

		/// <summary>Best compression, slowest.</summary>
		Ultra,
	}

	/// <summary>
	/// One entry of an archive.
	/// </summary>
	/// <param name="Path">The entry path as stored, with '/' separators.</param>
	/// <param name="IsDirectory">Whether the entry is a folder.</param>
	/// <param name="Size">The uncompressed size as declared by the archive (untrusted).</param>
	/// <param name="CompressedSize">The compressed size as declared by the archive.</param>
	/// <param name="Modified">The modification time, if stored.</param>
	/// <param name="IsEncrypted">Whether the entry is password protected.</param>
	/// <param name="LinkTarget">The symbolic link target when the entry is a link, otherwise null.</param>
	public sealed record ArchiveEntryInfo(
		string Path,
		bool IsDirectory,
		long Size,
		long CompressedSize,
		DateTime? Modified,
		bool IsEncrypted,
		string? LinkTarget = null);

	/// <summary>
	/// The content of an archive.
	/// </summary>
	/// <param name="Entries">The entries.</param>
	/// <param name="IsEncrypted">Whether at least one entry is encrypted.</param>
	/// <param name="IsSolid">Whether the archive is solid.</param>
	public sealed record ArchiveListing(IReadOnlyList<ArchiveEntryInfo> Entries, bool IsEncrypted, bool IsSolid);

	/// <summary>
	/// Zip bomb guards. A value of zero or less disables that guard.
	/// </summary>
	/// <param name="MaxTotalBytes">The maximum total uncompressed size.</param>
	/// <param name="MaxEntries">The maximum number of entries.</param>
	/// <param name="MaxRatio">The maximum overall compression ratio, only applied once the output exceeds <paramref name="RatioMinBytes"/>.</param>
	/// <param name="RatioMinBytes">The output size below which the ratio is not checked.</param>
	public sealed record ArchiveLimits(
		long MaxTotalBytes = 20L * 1024 * 1024 * 1024,
		long MaxEntries = 250_000,
		double MaxRatio = 1000,
		long RatioMinBytes = 256L * 1024 * 1024)
	{
		/// <summary>The default limits.</summary>
		public static ArchiveLimits Default { get; } = new();

		/// <summary>No limits.</summary>
		public static ArchiveLimits Unlimited { get; } = new(0, 0, 0, 0);
	}

	/// <summary>
	/// Describes a limit an archive exceeded. Passed to the user override prompt.
	/// </summary>
	/// <param name="Limit">Which limit (<c>"bytes"</c>, <c>"entries"</c> or <c>"ratio"</c>).</param>
	/// <param name="Actual">The measured value.</param>
	/// <param name="Allowed">The configured limit.</param>
	public sealed record ArchiveLimitViolation(string Limit, double Actual, double Allowed);

	/// <summary>
	/// Describes an item of the extraction that already exists at the destination.
	/// </summary>
	/// <param name="EntryPath">The entry path relative to the destination.</param>
	/// <param name="DestinationPath">The existing path.</param>
	/// <param name="IsDirectory">Whether the incoming item is a folder.</param>
	public sealed record ArchiveConflict(string EntryPath, string DestinationPath, bool IsDirectory);

	/// <summary>
	/// A snapshot of archive processing progress.
	/// </summary>
	public readonly record struct ArchiveProgress(long EntriesProcessed, long EntriesTotal, long BytesProcessed, long BytesTotal, string? CurrentEntry);

	/// <summary>
	/// Options for extraction.
	/// </summary>
	public sealed class ArchiveExtractOptions
	{
		/// <summary>The password, for encrypted archives.</summary>
		public string? Password { get; init; }

		/// <summary>The file name encoding for formats that do not store UTF-8 names (legacy zip), or null for automatic.</summary>
		public Encoding? FileNameEncoding { get; init; }

		/// <summary>The zip bomb guards.</summary>
		public ArchiveLimits Limits { get; init; } = ArchiveLimits.Default;

		/// <summary>Called when a limit is exceeded. Return true to continue anyway. When null the extraction fails.</summary>
		public Func<ArchiveLimitViolation, CancellationToken, Task<bool>>? LimitExceeded { get; init; }

		/// <summary>Called when an item already exists. When null existing items are never overwritten (they are skipped).</summary>
		public Func<ArchiveConflict, CancellationToken, Task<ConflictResolution>>? ResolveConflict { get; init; }

		/// <summary>Receives progress.</summary>
		public IProgress<ArchiveProgress>? Progress { get; init; }
	}

	/// <summary>
	/// Options for archive creation.
	/// </summary>
	public sealed class ArchiveCreateOptions
	{
		/// <summary>The format.</summary>
		public ArchiveFormat Format { get; init; } = ArchiveFormat.Zip;

		/// <summary>The compression level.</summary>
		public ArchiveCompressionLevel Level { get; init; } = ArchiveCompressionLevel.Normal;

		/// <summary>Called with items that cannot be read before anything is written. Return false to cancel. When null they are skipped.</summary>
		public Func<IReadOnlyList<string>, CancellationToken, Task<bool>>? ConfirmSkipped { get; init; }

		/// <summary>Receives progress.</summary>
		public IProgress<ArchiveProgress>? Progress { get; init; }
	}

	/// <summary>
	/// The outcome of an archive operation.
	/// </summary>
	/// <param name="Succeeded">Whether the operation completed.</param>
	/// <param name="Cancelled">Whether the user or the token cancelled it.</param>
	/// <param name="ItemsProcessed">Items written or extracted.</param>
	/// <param name="ItemsSkipped">Items skipped (conflicts, unsafe links, unreadable).</param>
	/// <param name="Error">A short error message when it failed.</param>
	/// <param name="Output">The produced archive path or the destination.</param>
	public sealed record ArchiveResult(bool Succeeded, bool Cancelled, long ItemsProcessed, long ItemsSkipped, string? Error = null, string? Output = null);

	/// <summary>
	/// Thrown when an archive is malicious (path traversal and similar).
	/// </summary>
	public class ArchiveSecurityException : Exception
	{
		/// <summary>Creates the exception.</summary>
		public ArchiveSecurityException(string message) : base(message) { }
	}

	/// <summary>
	/// Thrown when a zip bomb limit is exceeded and not overridden.
	/// </summary>
	public sealed class ArchiveLimitExceededException : ArchiveSecurityException
	{
		/// <summary>Creates the exception.</summary>
		public ArchiveLimitExceededException(ArchiveLimitViolation violation)
			: base($"Archive exceeds the {violation.Limit} limit ({violation.Actual:N0} > {violation.Allowed:N0}).")
		{
			Violation = violation;
		}

		/// <summary>The violated limit.</summary>
		public ArchiveLimitViolation Violation { get; }
	}

	/// <summary>
	/// Thrown when a password is missing or wrong.
	/// </summary>
	public sealed class ArchivePasswordException : Exception
	{
		/// <summary>Creates the exception.</summary>
		public ArchivePasswordException(string message, Exception? inner = null) : base(message, inner) { }
	}
}
