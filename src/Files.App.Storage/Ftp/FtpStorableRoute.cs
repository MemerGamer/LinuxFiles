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
			FtpManager.RememberUrlCredential(url);
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

		/// <summary>
		/// Whether an FTP reply means the server refused access (login or permission) rather than "no such file".
		/// 530 and 532 are always access failures; 550 is ambiguous, so its text decides.
		/// </summary>
		public static bool IsPermissionReply(string? code, string? message)
		{
			if (code is "530" or "532")
				return true;

			if (code != "550" || message is null)
				return false;

			// "No such file" wins over everything else
			if (message.Contains("no such", StringComparison.OrdinalIgnoreCase) ||
				message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
				message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
				return false;

			// Servers echo the path first ("/access.log: Permission denied"), so only the reason after it is matched
			var separator = message.LastIndexOf(": ", StringComparison.Ordinal);
			var reason = separator >= 0 ? message[(separator + 2)..] : message;

			return reason.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
				reason.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
				reason.Contains("not allowed", StringComparison.OrdinalIgnoreCase) ||
				reason.Contains("access", StringComparison.OrdinalIgnoreCase) ||
				reason.Contains("forbidden", StringComparison.OrdinalIgnoreCase);
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
