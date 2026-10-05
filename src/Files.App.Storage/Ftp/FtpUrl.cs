// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;

namespace Files.App.Storage
{
	/// <summary>
	/// A parsed ftp://, ftps:// or ftpes:// location. The path is kept raw: '?', '#' and '%' are valid file name characters.
	/// </summary>
	public sealed class FtpUrl
	{
		public string Scheme { get; }

		/// <summary>The host without IPv6 brackets.</summary>
		public string Host { get; }

		/// <summary>The explicit port, or <see langword="null"/> if the URL uses the scheme default.</summary>
		public ushort? ExplicitPort { get; }

		/// <summary>The absolute path on the server, "/" for the root.</summary>
		public string Path { get; }

		/// <summary>The decoded user name from the URL, if any.</summary>
		public string? UserName { get; }

		/// <summary>The decoded password from the URL, if any.</summary>
		public string? Password { get; }

		public ushort Port => ExplicitPort ?? (IsImplicitTls ? (ushort)990 : (ushort)21);

		public bool IsImplicitTls => Scheme.Equals("ftps", StringComparison.Ordinal);

		/// <summary>Whether TLS is requested explicitly by the scheme; plain ftp:// still upgrades when the server supports it.</summary>
		public bool IsExplicitTls => Scheme.Equals("ftpes", StringComparison.Ordinal);

		private FtpUrl(string scheme, string host, ushort? port, string path, string? userName, string? password)
		{
			Scheme = scheme;
			Host = host;
			ExplicitPort = port;
			Path = path;
			UserName = userName;
			Password = password;
		}

		/// <summary>
		/// The location without credentials, safe to store and display. The root has no trailing slash.
		/// </summary>
		public string ToId()
		{
			var host = Host.Contains(':') ? $"[{Host}]" : Host;
			var port = ExplicitPort is { } p ? $":{p.ToString(CultureInfo.InvariantCulture)}" : string.Empty;

			return $"{Scheme}://{host}{port}{(Path == "/" ? string.Empty : Path)}";
		}

		/// <summary>
		/// Scope key for credentials: scheme, host and port, so ftp:// credentials are never used for ftps:// or another port.
		/// </summary>
		public string GetCredentialKey()
			=> $"{Scheme}://{Host.ToLowerInvariant()}:{Port.ToString(CultureInfo.InvariantCulture)}";

		/// <summary>Never includes the user info.</summary>
		public override string ToString() => ToId();

		public NetworkCredential? GetCredential()
			=> UserName is null ? null : new NetworkCredential(UserName, Password ?? string.Empty);

		public static bool IsFtpScheme([NotNullWhen(true)] string? path)
		{
			return !string.IsNullOrEmpty(path) &&
				(path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
				path.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase) ||
				path.StartsWith("ftpes://", StringComparison.OrdinalIgnoreCase));
		}

		public static FtpUrl Parse(string url)
		{
			return TryParse(url, out var result) ? result : throw new UriFormatException("The FTP path is not valid.");
		}

		public static bool TryParse([NotNullWhen(true)] string? url, [NotNullWhen(true)] out FtpUrl? result)
		{
			result = null;
			if (!IsFtpScheme(url) || url.Contains('\0'))
				return false;

			url = url.Replace('\\', '/');
			var schemeEnd = url.IndexOf(Uri.SchemeDelimiter, StringComparison.Ordinal);
			var scheme = url[..schemeEnd].ToLowerInvariant();

			var authorityStart = schemeEnd + Uri.SchemeDelimiter.Length;
			var pathStart = url.IndexOf('/', authorityStart);
			var authority = pathStart < 0 ? url[authorityStart..] : url[authorityStart..pathStart];
			var path = pathStart < 0 ? "/" : url[pathStart..];

			string? userName = null, password = null;
			var at = authority.LastIndexOf('@');
			if (at >= 0)
			{
				var userInfo = authority[..at];
				authority = authority[(at + 1)..];
				var colon = userInfo.IndexOf(':');
				try
				{
					userName = Uri.UnescapeDataString(colon < 0 ? userInfo : userInfo[..colon]);
					password = colon < 0 ? null : Uri.UnescapeDataString(userInfo[(colon + 1)..]);
				}
				catch (UriFormatException)
				{
					return false;
				}
			}

			string host;
			string portText;
			if (authority.StartsWith('['))
			{
				var close = authority.IndexOf(']');
				if (close < 0)
					return false;

				host = authority[1..close];
				var rest = authority[(close + 1)..];
				if (rest.Length > 0 && !rest.StartsWith(':'))
					return false;

				portText = rest.Length > 0 ? rest[1..] : string.Empty;
				if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
					return false;
			}
			else
			{
				var colon = authority.LastIndexOf(':');
				host = colon < 0 ? authority : authority[..colon];
				portText = colon < 0 ? string.Empty : authority[(colon + 1)..];
				if (host.Contains(':') || host.Contains('['))
					return false;
			}

			if (host.Length == 0)
				return false;

			ushort? port = null;
			if (portText.Length > 0)
			{
				if (!ushort.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed == 0)
					return false;
				port = parsed;
			}

			result = new FtpUrl(scheme, host, port, path, userName, password);
			return true;
		}
	}
}
