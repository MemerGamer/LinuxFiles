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
		public async Task<StorableResult> TryGetAsync(string path, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (!FtpUrl.IsFtpScheme(path))
				return StorableResult.NotMine;

			// A malformed FTP URL is still ours, so it never falls through to the local route.
			if (!FtpUrl.TryParse(path, out var url))
				return StorableResult.NotFound;

			// The URL can contain a password, so neither it nor exception messages are logged or surfaced.
			var id = url.ToId();
			try
			{
				return StorableResult.Success(await _ftpStorageService.GetFolderAsync(id, cancellationToken));
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
			{
				// Not a folder; fall through to the file lookup
			}
			catch (Exception ex)
			{
				return ToFailure(ex);
			}

			try
			{
				return StorableResult.Success(await _ftpStorageService.GetFileAsync(id, cancellationToken));
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				return ToFailure(ex);
			}
		}

		private static StorableResult ToFailure(Exception ex)
		{
			return ex switch
			{
				DirectoryNotFoundException or FileNotFoundException => StorableResult.NotFound,
				UnauthorizedAccessException => StorableResult.AccessDenied,
				_ when ex.GetType().Name == "FtpAuthenticationException" => StorableResult.AccessDenied,
				_ => StorableResult.Error,
			};
		}
	}
}
