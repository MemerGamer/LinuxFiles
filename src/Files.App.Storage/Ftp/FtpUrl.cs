// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
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
		/// Scope key for credentials: scheme, host and port. Built from the same canonical <see cref="Host"/>, <see cref="Port"/>
		/// and scheme the connection uses, so the key and the connection target can never disagree.
		/// </summary>
		public string GetCredentialKey()
		{
			var host = Host.Contains(':') ? $"[{Host}]" : Host;
			return $"{Scheme}://{host}:{Port.ToString(CultureInfo.InvariantCulture)}";
		}

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

		/// <summary>
		/// Strict single-pass parser. Ambiguous input is rejected rather than interpreted: more than one '@' in the authority,
		/// backslashes, whitespace or control characters, '%', '#' or '?' in the authority, empty or malformed hosts and ports,
		/// non-canonical IPv4 forms, and ':' inside the user name. <see cref="Host"/> is canonical (lowercase, punycode,
		/// normalized IP literal) and is the only host used for both credential lookup and connecting.
		/// </summary>
		public static bool TryParse([NotNullWhen(true)] string? url, [NotNullWhen(true)] out FtpUrl? result)
		{
			result = null;
			if (!IsFtpScheme(url))
				return false;

			foreach (var c in url)
			{
				if (c < 0x20 || c == 0x7f)
					return false;
			}

			var schemeEnd = url.IndexOf(Uri.SchemeDelimiter, StringComparison.Ordinal);
			var scheme = url[..schemeEnd].ToLowerInvariant();

			var authorityStart = schemeEnd + Uri.SchemeDelimiter.Length;
			var pathStart = url.IndexOf('/', authorityStart);
			var authority = pathStart < 0 ? url[authorityStart..] : url[authorityStart..pathStart];
			var path = (pathStart < 0 ? "/" : url[pathStart..]).Replace('\\', '/');

			foreach (var c in authority)
			{
				if (c == '\\' || c == '#' || c == '?' || char.IsWhiteSpace(c))
					return false;
			}

			string? userName = null, password = null;
			var at = authority.IndexOf('@');
			if (at >= 0)
			{
				if (authority.IndexOf('@', at + 1) >= 0)
					return false;

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

				if (userName.Length == 0 || userName.Contains(':') || userName.Any(char.IsControl) || password?.Any(char.IsControl) == true)
					return false;
			}

			if (authority.Contains('%') || authority.Contains('@'))
				return false;

			string rawHost;
			string portText;
			if (authority.StartsWith('['))
			{
				var close = authority.IndexOf(']');
				if (close < 0)
					return false;

				rawHost = authority[1..close];
				var rest = authority[(close + 1)..];
				if (rest.Length > 0 && !rest.StartsWith(':'))
					return false;

				portText = rest.Length > 0 ? rest[1..] : null!;
				if (!IPAddress.TryParse(rawHost, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
					return false;
			}
			else
			{
				var colon = authority.IndexOf(':');
				rawHost = colon < 0 ? authority : authority[..colon];
				portText = colon < 0 ? null! : authority[(colon + 1)..];
				if (portText is not null && portText.Contains(':'))
					return false;
			}

			if (!TryCanonicalizeHost(rawHost, authority.StartsWith('['), out var host))
				return false;

			ushort? port = null;
			if (portText is not null)
			{
				if (portText.Length == 0 || !ushort.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed == 0)
					return false;
				port = parsed;
			}

			result = new FtpUrl(scheme, host, port, path, userName, password);
			return true;
		}

		private static bool TryCanonicalizeHost(string host, bool bracketed, [NotNullWhen(true)] out string? canonical)
		{
			canonical = null;
			if (host.Length == 0 || host.Contains('%'))
				return false;

			if (bracketed)
			{
				var address = IPAddress.Parse(host);
				canonical = address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();
				return true;
			}

			// Anything numeric must be a strict dotted quad: shorthand and octal/hex forms mean different servers to different resolvers.
			if (host.All(static c => char.IsAsciiDigit(c) || c == '.'))
			{
				if (!IPAddress.TryParse(host, out var v4) || v4.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || v4.ToString() != host)
					return false;

				canonical = host;
				return true;
			}

			if (host.StartsWith('.') || host.Contains("..", StringComparison.Ordinal) || host.Contains(':') || host.Contains('[') || host.Contains(']'))
				return false;

			host = host.TrimEnd('.');
			foreach (var label in host.Split('.'))
			{
				if (label.Length == 0 || label.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
					return false;
			}

			try
			{
				host = new IdnMapping().GetAscii(host).ToLowerInvariant();
			}
			catch (ArgumentException)
			{
				return false;
			}

			foreach (var c in host)
			{
				if (!(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '.' || c == '_'))
					return false;
			}

			canonical = host;
			return host.Length > 0;
		}
	}
}
