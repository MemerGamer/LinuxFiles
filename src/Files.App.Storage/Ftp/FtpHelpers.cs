// Copyright (c) Files Community
// Licensed under the MIT License.

using FluentFTP;

namespace Files.App.Storage
{
	internal static class FtpHelpers
	{
		public static string GetFtpPath(string path)
			=> FtpUrl.Parse(path).Path;

		public static Task EnsureConnectedAsync(this AsyncFtpClient ftpClient, CancellationToken cancellationToken = default)
		{
			return ftpClient.IsConnected ? Task.CompletedTask : ftpClient.Connect(cancellationToken);
		}

		public static string GetFtpHost(string path)
			=> FtpUrl.Parse(path).Host;

		public static ushort GetFtpPort(string path)
			=> FtpUrl.Parse(path).Port;

		public static bool IsSameFtpPath(string firstPath, string secondPath)
		{
			return
				string.Equals(GetFtpHost(firstPath), GetFtpHost(secondPath), StringComparison.OrdinalIgnoreCase) &&
				GetFtpPort(firstPath) == GetFtpPort(secondPath) &&
				string.Equals(GetFtpPath(firstPath), GetFtpPath(secondPath), StringComparison.Ordinal);
		}

		public static AsyncFtpClient GetFtpClient(string ftpPath)
		{
			var url = FtpUrl.Parse(ftpPath);
			var credentials = url.GetCredential()
				?? FtpManager.Credentials.GetValueOrDefault(url.Host)
				?? FtpManager.Anonymous;

			var client = new AsyncFtpClient(url.Host, credentials, url.Port);

			// Prefer TLS: ftps:// is implicit, ftpes:// requires explicit TLS, plain ftp:// upgrades when the server supports it.
			client.Config.EncryptionMode = url.IsImplicitTls
				? FtpEncryptionMode.Implicit
				: url.IsExplicitTls ? FtpEncryptionMode.Explicit : FtpEncryptionMode.Auto;

			return client;
		}
	}
}
