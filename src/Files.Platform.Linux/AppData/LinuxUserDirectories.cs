// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Text;
using System;
using Files.Platform.Abstractions;

namespace Files.Platform.Linux
{
	/// <summary>
	/// Resolves user folders from <c>user-dirs.dirs</c> (xdg-user-dirs) with fallback to the default names.
	/// </summary>
	public sealed class LinuxUserDirectories : IUserDirectories
	{
		/// <summary>
		/// Initializes a new instance using the process environment.
		/// </summary>
		public LinuxUserDirectories()
			: this(Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
		{
		}

		/// <summary>
		/// Initializes a new instance with injected environment access.
		/// </summary>
		public LinuxUserDirectories(Func<string, string?> getEnvironmentVariable, string home)
		{
			Home = home;
			_getEnvironmentVariable = getEnvironmentVariable;

			var configHome = getEnvironmentVariable("XDG_CONFIG_HOME");
			if (string.IsNullOrEmpty(configHome) || !Path.IsPathRooted(configHome))
				configHome = Path.Combine(home, ".config");

			var configured = ParseFile(Path.Combine(configHome, "user-dirs.dirs"), home);

			Desktop = Resolve(configured, "DESKTOP", "Desktop");
			Documents = Resolve(configured, "DOCUMENTS", "Documents");
			Downloads = Resolve(configured, "DOWNLOAD", "Downloads");
			Music = Resolve(configured, "MUSIC", "Music");
			Pictures = Resolve(configured, "PICTURES", "Pictures");
			Videos = Resolve(configured, "VIDEOS", "Videos");
			Templates = Resolve(configured, "TEMPLATES", "Templates");
			PublicShare = Resolve(configured, "PUBLICSHARE", "Public");
		}

		/// <inheritdoc/>
		public string Home { get; }

		/// <inheritdoc/>
		public string Desktop { get; }

		/// <inheritdoc/>
		public string Documents { get; }

		/// <inheritdoc/>
		public string Downloads { get; }

		/// <inheritdoc/>
		public string Music { get; }

		/// <inheritdoc/>
		public string Pictures { get; }

		/// <inheritdoc/>
		public string Videos { get; }

		/// <inheritdoc/>
		public string Templates { get; }

		/// <inheritdoc/>
		public string PublicShare { get; }

		private readonly Func<string, string?> _getEnvironmentVariable;

		private string Resolve(Dictionary<string, string> configured, string key, string defaultName)
		{
			// XDG_<NAME>_DIR in the environment (set by some sandboxes and sessions) wins over the file.
			if (_getEnvironmentVariable($"XDG_{key}_DIR") is { Length: > 0 } fromEnv
				&& Path.IsPathRooted(fromEnv) && !IsHome(fromEnv))
				return fromEnv;

			// Like xdg-user-dirs, a value equal to $HOME means the directory is disabled.
			return configured.TryGetValue(key, out var path) && !IsHome(path)
				? path
				: Path.Combine(Home, defaultName);
		}

		private bool IsHome(string path)
			=> Path.TrimEndingDirectorySeparator(path) == Path.TrimEndingDirectorySeparator(Home);

		/// <summary>
		/// Parses user-dirs.dirs content into a map of name (without the XDG_ prefix and _DIR suffix) to absolute path.
		/// </summary>
		internal static Dictionary<string, string> Parse(string content, string home)
		{
			var result = new Dictionary<string, string>(StringComparer.Ordinal);

			foreach (var rawLine in content.Split('\n'))
			{
				var line = rawLine.Trim();
				if (line.Length == 0 || line[0] == '#')
					continue;

				var eq = line.IndexOf('=');
				if (eq <= 0)
					continue;

				var name = line[..eq].Trim();
				if (!name.StartsWith("XDG_", StringComparison.Ordinal) || !name.EndsWith("_DIR", StringComparison.Ordinal))
					continue;

				var value = ParseValue(line[(eq + 1)..].Trim(), home);
				if (value is null || !Path.IsPathRooted(value))
					continue;

				result[name["XDG_".Length..^"_DIR".Length]] = value;
			}

			return result;
		}

		private static string? ParseValue(string raw, string home)
		{
			if (raw.Length == 0)
				return null;

			StringBuilder sb = new();

			if (raw[0] == '"')
			{
				var i = 1;
				for (; i < raw.Length && raw[i] != '"'; i++)
				{
					if (raw[i] == '\\' && i + 1 < raw.Length)
						i++;
					sb.Append(raw[i]);
				}

				if (i >= raw.Length)
					return null; // Unterminated quote
			}
			else if (raw[0] == '\'')
			{
				var end = raw.IndexOf('\'', 1);
				// Single-quoted values are literal, so no $HOME expansion.
				return end < 0 ? null : raw[1..end];
			}
			else
			{
				var end = raw.IndexOfAny([' ', '\t', '#']);
				sb.Append(end < 0 ? raw : raw[..end]);
			}

			var value = sb.ToString();
			if (value == "$HOME")
				return home;
			if (value.StartsWith("$HOME/", StringComparison.Ordinal))
				return home.TrimEnd('/') + value["$HOME".Length..];

			return value;
		}

		private static Dictionary<string, string> ParseFile(string path, string home)
		{
			try
			{
				return Parse(File.ReadAllText(path), home);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return [];
			}
		}
	}
}
