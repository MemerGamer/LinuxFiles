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
			var key = url.GetCredentialKey();

			// Saved or session credentials win; credentials embedded in the URL are only a last resort and never replace them.
			// Only the exact scheme+host+port key is consulted; there is no host-only fallback.
			var credentials = FtpManager.Credentials.TryGetValue(key, out var known) ? known : FtpManager.Anonymous;
			var isAnonymous = ReferenceEquals(credentials, FtpManager.Anonymous);

			var client = new AsyncFtpClient(url.Host, credentials, url.Port);

			// Certificates are always validated (no accept-all callback is ever attached).
			client.Config.ValidateAnyCertificate = false;

			if (url.IsImplicitTls)
			{
				client.Config.EncryptionMode = FtpEncryptionMode.Implicit;
				client.Config.DataConnectionEncryption = true;
			}
			else if (FtpManager.RequiresTls(url, isAnonymous))
			{
				// ftpes:// and any password over plain ftp:// require TLS: no fallback to cleartext
				// unless the user explicitly approved an unencrypted connection to this host and port.
				client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
				client.Config.DataConnectionEncryption = true;
			}
			else
			{
				// Anonymous or explicitly approved: still use TLS when the server offers it.
				client.Config.EncryptionMode = FtpEncryptionMode.Auto;
			}

			return client;
		}
	}
}
