// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Core.Storage;
using Files.Core.Storage.Contracts;
using OwlCore.Storage;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.Storage
{
	/// <summary>
	/// Resolves ftp://, ftps:// and ftpes:// locations through <see cref="IFtpStorageService"/>.
	/// </summary>
	public sealed class FtpStorableRoute : IStorableRoute
	{
		/// <summary>
		/// Consulted before the catch-all local route.
		/// </summary>
		public const int DefaultOrder = 1_000;

		private readonly IFtpStorageService _ftpStorageService;

		public FtpStorableRoute(IFtpStorageService ftpStorageService)
		{
			_ftpStorageService = ftpStorageService;
		}

		/// <inheritdoc/>
		public int Order => DefaultOrder;

		/// <inheritdoc/>
		public bool CanResolve(string path)
			=> FtpUrl.TryParse(path, out _);

		/// <inheritdoc/>
		public async Task<IStorable?> TryGetAsync(string path, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (!FtpUrl.TryParse(path, out var url))
				return null;

			// The URL can contain a password, so neither it nor exception messages are logged or surfaced.
			var id = url.ToId();
			try
			{
				return await _ftpStorageService.GetFolderAsync(id, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
			{
				// Not a folder; fall through to the file lookup
			}
			catch (Exception)
			{
				return null;
			}

			try
			{
				return await _ftpStorageService.GetFileAsync(id, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
