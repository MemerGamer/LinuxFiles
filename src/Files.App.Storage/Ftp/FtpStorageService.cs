// Copyright (c) Files Community
// Licensed under the MIT License.

using FluentFTP;
using Files.Platform.Abstractions.Secrets;
using System.IO;

namespace Files.App.Storage
{
	/// <inheritdoc cref="IFtpStorageService"/>
	public sealed class FtpStorageService : IFtpStorageService
	{
		public FtpStorageService(ISecretStore? secretStore = null)
		{
			if (secretStore is not null)
				FtpManager.SecretStore ??= secretStore;
		}

		/// <inheritdoc/>
		public async Task<IFolder> GetFolderAsync(string id, CancellationToken cancellationToken = default)
		{
			var url = FtpUrl.Parse(id);
			RememberUrlCredential(url);
			id = url.ToId();

			using var ftpClient = FtpHelpers.GetFtpClient(id);
			await ftpClient.EnsureConnectedAsync(cancellationToken);

			var ftpPath = FtpHelpers.GetFtpPath(id);
			var item = await ftpClient.GetObjectInfo(ftpPath, token: cancellationToken);
			if (item is null || item.Type != FtpObjectType.Directory)
				throw new DirectoryNotFoundException("Directory was not found from path.");

			return new FtpStorageFolder(id, item.Name, null);
		}

		/// <inheritdoc/>
		public async Task<IFile> GetFileAsync(string id, CancellationToken cancellationToken = default)
		{
			var url = FtpUrl.Parse(id);
			RememberUrlCredential(url);
			id = url.ToId();

			using var ftpClient = FtpHelpers.GetFtpClient(id);
			await ftpClient.EnsureConnectedAsync(cancellationToken);

			var ftpPath = FtpHelpers.GetFtpPath(id);
			var item = await ftpClient.GetObjectInfo(ftpPath, token: cancellationToken);
			if (item is null || item.Type != FtpObjectType.File)
				throw new FileNotFoundException("File was not found from path.");

			return new FtpStorageFile(id, item.Name, null);
		}

		// Credentials typed into a URL stay in memory for the session; they are never persisted or kept in item ids.
		private static void RememberUrlCredential(FtpUrl url)
		{
			if (url.GetCredential() is { } credential)
				FtpManager.Credentials.SetFromUrl(url.GetCredentialKey(), credential);
		}
	}
}
