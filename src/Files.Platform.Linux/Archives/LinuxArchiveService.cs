// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Files.Platform.Abstractions.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Files.Platform.Linux.Archives
{
	/// <summary>
	/// Archive service backed by the managed SharpCompress library. Writes 7z through the system 7-Zip binary when present.
	/// </summary>
	public sealed partial class LinuxArchiveService : IArchiveService
	{
		// Longest first so ".tar.gz" wins over ".gz"
		private static readonly string[] ArchiveExtensions =
		[
			".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst", ".tar.lz",
			".tgz", ".tbz2", ".tbz", ".txz", ".tzst",
			".zip", ".jar", ".mrpack", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".zst", ".lz",
		];

		private readonly ISevenZipRunner sevenZip;

		/// <summary>
		/// Creates the service using the system 7-Zip binary when available.
		/// </summary>
		public LinuxArchiveService() : this(new SevenZipProcessRunner())
		{
		}

		/// <summary>
		/// Creates the service with the given 7-Zip runner.
		/// </summary>
		public LinuxArchiveService(ISevenZipRunner sevenZip)
		{
			this.sevenZip = sevenZip;
		}

		/// <inheritdoc/>
		public IReadOnlyList<ArchiveFormat> CreatableFormats
		{
			get
			{
				var formats = new List<ArchiveFormat> { ArchiveFormat.Zip, ArchiveFormat.Tar, ArchiveFormat.TarGz, ArchiveFormat.TarBz2 };
				if (sevenZip.FindBinary() is not null)
					formats.Add(ArchiveFormat.SevenZip);

				return formats;
			}
		}

		/// <inheritdoc/>
		public bool IsArchiveFileName(string path)
			=> FindExtension(path) is not null;

		/// <inheritdoc/>
		public string GetExtension(ArchiveFormat format) => format switch
		{
			ArchiveFormat.Zip => ".zip",
			ArchiveFormat.Tar => ".tar",
			ArchiveFormat.TarGz => ".tar.gz",
			ArchiveFormat.TarBz2 => ".tar.bz2",
			ArchiveFormat.SevenZip => ".7z",
			_ => throw new ArgumentOutOfRangeException(nameof(format)),
		};

		/// <inheritdoc/>
		public string GetDefaultExtractFolderName(string archivePath)
		{
			var name = Path.GetFileName(archivePath);
			var extension = FindExtension(name);
			var stem = extension is null ? Path.GetFileNameWithoutExtension(name) : name[..^extension.Length];
			return string.IsNullOrWhiteSpace(stem) ? "Extracted" : stem;
		}

		/// <inheritdoc/>
		public Task<ArchiveListing> ListAsync(string archivePath, string? password = null, Encoding? fileNameEncoding = null, CancellationToken cancellationToken = default)
		{
			return Task.Run(() =>
			{
				using var archive = OpenArchive(archivePath, password, fileNameEncoding, BrowsingLimits.MaxTotalBytes, cancellationToken);
				var fallback = GetDefaultExtractFolderName(archivePath);
				var entries = new List<ArchiveEntryInfo>();
				var encrypted = false;
				var guard = new ExtractionGuard(BrowsingLimits, null, new FileInfo(archivePath).Length, cancellationToken);
				long bytes = 0;
				var nameChars = 0;

				foreach (var (entry, _) in archive.Entries(headersOnly: true))
				{
					cancellationToken.ThrowIfCancellationRequested();
					guard.CheckEntryCount(entries.Count + 1);
					bytes = checked(bytes + Math.Max(entry.Size, 0));
					guard.CheckDeclared(bytes);
					nameChars = checked(nameChars + (entry.Key?.Length ?? fallback.Length));
					if (nameChars > 1024 * 1024)
						throw new ArchiveSecurityException("The archive name limit was exceeded.");
					encrypted |= entry.IsEncrypted;
					entries.Add(new ArchiveEntryInfo(
						(entry.Key ?? fallback).Replace('\\', '/'),
						entry.IsDirectory,
						Math.Max(entry.Size, 0),
						Math.Max(entry.CompressedSize, 0),
						entry.Modified,
						entry.IsEncrypted,
						IsLink(entry) ? entry.LinkTarget ?? string.Empty : null));
				}

				return new ArchiveListing(entries, encrypted, archive.IsSolid);
			}, cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<bool> IsEncryptedAsync(string archivePath, CancellationToken cancellationToken = default)
		{
			try
			{
				return (await ListAsync(archivePath, null, null, cancellationToken).ConfigureAwait(false)).IsEncrypted;
			}
			catch (ArchivePasswordException)
			{
				// Encrypted headers
				return true;
			}
		}

		private static string? FindExtension(string path)
		{
			var name = Path.GetFileName(path);
			foreach (var extension in ArchiveExtensions)
			{
				if (name.Length > extension.Length && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
					return name[^extension.Length..];
			}

			return null;
		}

		private static ArchiveSource OpenArchive(string path, string? password, Encoding? encoding, long? maxBytes = null, CancellationToken cancellationToken = default)
		{
			try
			{
				return ArchiveSource.Open(path, password, encoding, maxBytes, cancellationToken);
			}
			catch (Exception ex) when (ex is CryptographicException or System.Security.Cryptography.CryptographicException)
			{
				throw new ArchivePasswordException("The archive is encrypted and the password is missing or wrong.", ex);
			}
		}
	}
}
