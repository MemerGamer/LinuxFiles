// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Icons;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Icons
{
	/// <summary>
	/// Configuration for <see cref="LinuxIconThemeProvider"/>.
	/// </summary>
	public sealed class LinuxIconThemeOptions
	{
		/// <summary>
		/// Gets or sets the directories that contain theme folders, in priority order.
		/// </summary>
		public IList<string> IconDirectories { get; set; } = [];

		/// <summary>
		/// Gets or sets the directories searched last for loose icons (pixmaps).
		/// </summary>
		public IList<string> PixmapDirectories { get; set; } = [];

		/// <summary>
		/// Gets or sets the XDG config home used to read the desktop's icon theme setting.
		/// </summary>
		public string ConfigHome { get; set; } = string.Empty;

		/// <summary>
		/// Gets or sets the value of <c>XDG_CURRENT_DESKTOP</c>.
		/// </summary>
		public string? CurrentDesktop { get; set; }

		/// <summary>
		/// Gets or sets an explicit theme name that skips detection.
		/// </summary>
		public string? ThemeName { get; set; }

		/// <summary>
		/// Creates options from the XDG environment variables.
		/// </summary>
		public static LinuxIconThemeOptions FromEnvironment()
		{
			var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
			if (string.IsNullOrEmpty(dataHome))
				dataHome = Path.Combine(home, ".local", "share");

			var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
			if (string.IsNullOrEmpty(configHome))
				configHome = Path.Combine(home, ".config");

			var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
			if (string.IsNullOrEmpty(dataDirs))
				dataDirs = "/usr/local/share:/usr/share";

			var icons = new List<string> { Path.Combine(home, ".icons"), Path.Combine(dataHome, "icons") };
			var pixmaps = new List<string>();
			foreach (var dir in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
			{
				icons.Add(Path.Combine(dir, "icons"));
				pixmaps.Add(Path.Combine(dir, "pixmaps"));
			}

			if (!pixmaps.Contains("/usr/share/pixmaps"))
				pixmaps.Add("/usr/share/pixmaps");

			return new LinuxIconThemeOptions
			{
				IconDirectories = icons,
				PixmapDirectories = pixmaps,
				ConfigHome = configHome,
				CurrentDesktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
			};
		}
	}

	/// <summary>
	/// Icon name resolver implementing the freedesktop Icon Theme Specification.
	/// </summary>
	public sealed class LinuxIconThemeProvider : IIconThemeProvider
	{
		private static readonly string[] FallbackThemes = ["breeze", "Adwaita", "gnome", "Papirus"];
		private static readonly string[] Extensions = [".svg", ".png", ".xpm"];

		private readonly LinuxIconThemeOptions _options;
		private readonly Lazy<string> _themeName;
		private readonly ConcurrentDictionary<string, IconThemeIndex?> _themes = new(StringComparer.Ordinal);
		private readonly ConcurrentDictionary<(string Name, int Size, int Scale), IconLookupResult?> _cache = new();

		/// <summary>
		/// Initializes a new instance using the process environment.
		/// </summary>
		public LinuxIconThemeProvider() : this(LinuxIconThemeOptions.FromEnvironment())
		{
		}

		/// <summary>
		/// Initializes a new instance with explicit options.
		/// </summary>
		public LinuxIconThemeProvider(LinuxIconThemeOptions options)
		{
			_options = options;
			_themeName = new Lazy<string>(DetectTheme);
		}

		/// <inheritdoc/>
		public string CurrentThemeName => _themeName.Value;

		/// <inheritdoc/>
		public Task<IconLookupResult?> ResolveIconAsync(string iconName, uint size, int scale = 1, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.Run(() => Resolve(iconName, (int)size, Math.Max(1, scale)), cancellationToken);
		}

		/// <inheritdoc/>
		public Task<IconLookupResult?> ResolveIconAsync(IReadOnlyList<string> iconNames, uint size, int scale = 1, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.Run(() =>
			{
				foreach (var name in iconNames)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var result = Resolve(name, (int)size, Math.Max(1, scale));
					if (result is not null)
						return result;
				}

				return null;
			}, cancellationToken);
		}

		/// <inheritdoc/>
		public Task<IReadOnlyList<IconLookupResult>> ResolveIconCandidatesAsync(IReadOnlyList<string> iconNames, uint size, int scale = 1, CancellationToken cancellationToken = default)
		{
			return Task.Run<IReadOnlyList<IconLookupResult>>(() =>
			{
				var results = new List<IconLookupResult>();
				var paths = new HashSet<string>(StringComparer.Ordinal);
				foreach (var name in iconNames)
				{
					foreach (var result in FindCandidates(name, (int)size, Math.Max(1, scale)))
					{
						cancellationToken.ThrowIfCancellationRequested();
						if (paths.Add(result.Path))
							results.Add(result);
					}
				}

				return results;
			}, cancellationToken);
		}

		private IconLookupResult? Resolve(string iconName, int size, int scale)
		{
			if (string.IsNullOrEmpty(iconName))
				return null;

			if (Path.IsPathRooted(iconName))
				return IconExists(iconName) ? ToResult(iconName) : null;

			return _cache.GetOrAdd((iconName, size, scale), key => FindWithFallbackNames(key.Name, key.Size, key.Scale));
		}

		private IconLookupResult? FindWithFallbackNames(string name, int size, int scale)
		{
			foreach (var result in FindCandidates(name, size, scale))
				return result;

			return null;
		}

		private IEnumerable<IconLookupResult> FindCandidates(string name, int size, int scale)
		{
			if (string.IsNullOrEmpty(name))
				yield break;

			if (Path.IsPathRooted(name))
			{
				if (IconExists(name))
					yield return ToResult(name);
				yield break;
			}

			while (true)
			{
				var visited = new HashSet<string>(StringComparer.Ordinal);
				foreach (var theme in new[] { CurrentThemeName, "hicolor" }.Concat(FallbackThemes))
				{
					foreach (var result in FindInTheme(theme, name, size, scale, visited))
						yield return result;
				}

				foreach (var dir in _options.IconDirectories.Concat(_options.PixmapDirectories))
				{
					foreach (var result in FindFiles(dir, name))
						yield return result;
				}

				// "folder-documents" falls back to "folder" when the theme has no specific icon.
				var dash = name.LastIndexOf('-');
				if (dash <= 0)
					yield break;

				name = name[..dash];
			}
		}

		private IEnumerable<IconLookupResult> FindInTheme(string theme, string name, int size, int scale, HashSet<string> visited)
		{
			if (!visited.Add(theme) || GetTheme(theme) is not { } index)
				yield break;

			// Without an exact match, scalable artwork beats resampling a raster frame
			foreach (var directory in index.Directories.OrderBy(d => d.Matches(size, scale) ? 0 : d.Type == IconDirectoryType.Scalable ? 1 : 2).ThenBy(d => d.Distance(size, scale)))
			{
				foreach (var baseDir in _options.IconDirectories)
				{
					foreach (var result in FindFiles(Path.Combine(baseDir, theme, directory.Path), name))
						yield return result;
				}
			}

			foreach (var parent in index.Inherits)
			{
				foreach (var result in FindInTheme(parent, name, size, scale, visited))
					yield return result;
			}
		}

		private static IEnumerable<IconLookupResult> FindFiles(string directory, string name)
		{
			foreach (var ext in Extensions)
			{
				var file = Path.Combine(directory, name + ext);
				if (IconExists(file))
					yield return ToResult(file);
			}
		}

		private static bool IconExists(string path)
		{
			try
			{
				var file = new FileInfo(path);
				return file.Exists && (file.LinkTarget is null || file.ResolveLinkTarget(true)?.Exists == true);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return false;
			}
		}

		private IconThemeIndex? GetTheme(string theme)
		{
			return _themes.GetOrAdd(theme, t =>
			{
				foreach (var baseDir in _options.IconDirectories)
				{
					var indexPath = Path.Combine(baseDir, t, "index.theme");
					try
					{
						if (File.Exists(indexPath))
							return IconThemeIndex.Parse(File.ReadAllText(indexPath));
					}
					catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
					{
					}
				}

				return null;
			});
		}

		private static IconLookupResult ToResult(string path) =>
			new(path, path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));

		private string DetectTheme()
		{
			var candidates = new List<string?>();
			if (!string.IsNullOrEmpty(_options.ThemeName))
				candidates.Add(_options.ThemeName);

			var isKde = _options.CurrentDesktop?.Contains("KDE", StringComparison.OrdinalIgnoreCase) == true;
			string? Kde() => ReadIni(Path.Combine(_options.ConfigHome, "kdeglobals"), "Icons", "Theme");
			string? Gtk() => ReadIni(Path.Combine(_options.ConfigHome, "gtk-4.0", "settings.ini"), "Settings", "gtk-icon-theme-name")
				?? ReadIni(Path.Combine(_options.ConfigHome, "gtk-3.0", "settings.ini"), "Settings", "gtk-icon-theme-name");

			if (isKde)
			{
				candidates.Add(Kde());
				candidates.Add(Gtk());
			}
			else
			{
				candidates.Add(Gtk());
				candidates.Add(Kde());
			}

			candidates.Add("Adwaita");
			candidates.Add("breeze");

			foreach (var candidate in candidates)
			{
				if (!string.IsNullOrEmpty(candidate) && GetTheme(candidate) is not null)
					return candidate;
			}

			return "hicolor";
		}

		private static string? ReadIni(string file, string section, string key)
		{
			try
			{
				if (!File.Exists(file))
					return null;

				var inSection = false;
				foreach (var raw in File.ReadLines(file))
				{
					var line = raw.Trim();
					if (line.StartsWith('['))
					{
						inSection = line == "[" + section + "]";
						continue;
					}

					if (!inSection)
						continue;

					var eq = line.IndexOf('=');
					if (eq > 0 && line[..eq].Trim() == key)
					{
						var value = line[(eq + 1)..].Trim();
						return value.Length == 0 ? null : value;
					}
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}

			return null;
		}
	}
}
