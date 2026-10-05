// Copyright (c) Files Community
// Licensed under the MIT License.

using FluentFTP;
using System.Diagnostics.CodeAnalysis;

namespace Files.App.Utils.Storage
{
	public static class FtpHelpers
	{
		public static async Task<bool> EnsureConnectedAsync(this AsyncFtpClient ftpClient)
		{
			if (!ftpClient.IsConnected)
			{
				await ftpClient.Connect();
			}

			return true;
		}

		public static bool IsFtpPath([NotNullWhen(true)] string? path)
		{
			if (!string.IsNullOrEmpty(path))
			{
				return path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase)
					|| path.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase)
					|| path.StartsWith("ftpes://", StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}

		public static bool VerifyFtpPath(string path)
			=> Files.App.Storage.FtpUrl.TryParse(path, out _);

		public static string GetFtpHost(string path)
			=> Files.App.Storage.FtpUrl.Parse(path).Host;

		public static ushort GetFtpPort(string path)
			=> Files.App.Storage.FtpUrl.Parse(path).Port;

		public static string GetFtpAuthority(string path)
		{
			if (!Files.App.Storage.FtpUrl.TryParse(path, out var url))
				return string.Empty;

			var host = url.Host.Contains(':') ? $"[{url.Host}]" : url.Host;
			return url.ExplicitPort is { } port ? $"{host}:{port}" : host;
		}

		public static string GetFtpPath(string path)
		{
			path = path.Replace('\\', '/');
			var schemaIndex = path.IndexOf("://", StringComparison.Ordinal) + 3;
			var hostIndex = path.IndexOf('/', schemaIndex);
			return hostIndex == -1 ? "/" : path.Substring(hostIndex);
		}

		public static int GetRootIndex(string path)
		{
			path = path.Replace('\\', '/');
			var schemaIndex = path.IndexOf("://", StringComparison.Ordinal) + 3;
			return path.IndexOf('/', schemaIndex);
		}
	}
}
