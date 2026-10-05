// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Archives
{
	/// <summary>
	/// Reads, extracts, tests and creates archives. Archive content is untrusted: implementations must refuse path traversal,
	/// unsafe links and zip bombs, and never overwrite existing items without asking.
	/// </summary>
	public interface IArchiveService
	{
		/// <summary>
		/// The formats <see cref="CreateAsync"/> can write on this machine.
		/// </summary>
		IReadOnlyList<ArchiveFormat> CreatableFormats { get; }

		/// <summary>
		/// Whether the file name looks like an archive that can be opened.
		/// </summary>
		bool IsArchiveFileName(string path);

		/// <summary>
		/// Returns the conventional file extension (with the dot) of the format.
		/// </summary>
		string GetExtension(ArchiveFormat format);

		/// <summary>
		/// Returns the suggested folder name for extracting the archive (its name without the archive extension).
		/// </summary>
		string GetDefaultExtractFolderName(string archivePath);

		/// <summary>
		/// Lists the entries. Fails with <see cref="ArchivePasswordException"/> if the headers are encrypted and the password is missing or wrong.
		/// </summary>
		Task<ArchiveListing> ListAsync(string archivePath, string? password = null, Encoding? fileNameEncoding = null, CancellationToken cancellationToken = default);

		/// <summary>Opens one safe entry as a caller-owned, read-only stream with fixed browsing size and ratio limits.</summary>
		Task<Stream> OpenEntryAsync(string archivePath, string entryPath, string? password = null, CancellationToken cancellationToken = default);

		/// <summary>Whether existing archives can be modified through this service.</summary>
		bool CanWriteEntries => false;

		/// <summary>Lists untrusted preview headers with bounded input, expanded bytes and entry count, without extracting files.</summary>
		Task<ArchiveListing> ListPreviewAsync(string archivePath, CancellationToken cancellationToken = default);

		/// <summary>
		/// Whether any entry needs a password.
		/// </summary>
		Task<bool> IsEncryptedAsync(string archivePath, CancellationToken cancellationToken = default);

		/// <summary>
		/// Extracts everything into <paramref name="destinationFolder"/> (created if missing). Used for "extract here" (the archive's
		/// folder) and "extract to folder" (any folder, such as <see cref="GetDefaultExtractFolderName"/> below the archive's folder).
		/// Content is staged in a temporary folder next to the destination and then moved in, asking
		/// <see cref="ArchiveExtractOptions.ResolveConflict"/> for existing items.
		/// </summary>
		Task<ArchiveResult> ExtractAsync(string archivePath, string destinationFolder, ArchiveExtractOptions? options = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Reads every entry to verify the archive (CRCs, decryption) without writing to disk.
		/// </summary>
		Task<ArchiveResult> TestAsync(string archivePath, string? password = null, ArchiveLimits? limits = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Creates an archive from files and folders. The archive is written to a temporary file and renamed, never replacing an existing file.
		/// </summary>
		Task<ArchiveResult> CreateAsync(IReadOnlyList<string> sources, string archivePath, ArchiveCreateOptions? options = null, CancellationToken cancellationToken = default);
	}
}
