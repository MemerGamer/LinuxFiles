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
		private static readonly string[] FallbackThemes = ["Adwaita", "breeze", "gnome", "Papirus"];
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

		private IconLookupResult? Resolve(string iconName, int size, int scale)
		{
			if (string.IsNullOrEmpty(iconName))
				return null;

			if (Path.IsPathRooted(iconName))
				return File.Exists(iconName) ? ToResult(iconName) : null;

			return _cache.GetOrAdd((iconName, size, scale), key => FindWithFallbackNames(key.Name, key.Size, key.Scale));
		}

		private IconLookupResult? FindWithFallbackNames(string name, int size, int scale)
		{
			// "folder-documents" falls back to "folder" when the theme has no specific icon.
			var current = name;
			while (true)
			{
				var result = FindIcon(current, size, scale);
				if (result is not null)
					return result;

				var dash = current.LastIndexOf('-');
				if (dash <= 0)
					return null;

				current = current[..dash];
			}
		}

		private IconLookupResult? FindIcon(string name, int size, int scale)
		{
			var visited = new HashSet<string>(StringComparer.Ordinal);
			var result = FindInTheme(CurrentThemeName, name, size, scale, visited) ?? FindInTheme("hicolor", name, size, scale, visited);
			foreach (var fallbackTheme in FallbackThemes)
				result ??= FindInTheme(fallbackTheme, name, size, scale, visited);

			return result ?? FindFallback(name);
		}

		private IconLookupResult? FindInTheme(string theme, string name, int size, int scale, HashSet<string> visited)
		{
			if (!visited.Add(theme))
				return null;

			var index = GetTheme(theme);
			if (index is null)
				return null;

			var path = LookupInTheme(theme, index, name, size, scale);
			if (path is not null)
				return ToResult(path);

			foreach (var parent in index.Inherits)
			{
				var inherited = FindInTheme(parent, name, size, scale, visited);
				if (inherited is not null)
					return inherited;
			}

			return null;
		}

		private string? LookupInTheme(string theme, IconThemeIndex index, string name, int size, int scale)
		{
			// Keep the selected theme, but prefer its scalable artwork over enlarging raster frames.
			foreach (var directory in index.Directories.Where(d => d.Type == IconDirectoryType.Scalable))
			{
				foreach (var baseDir in _options.IconDirectories)
				{
					var svg = Path.Combine(baseDir, theme, directory.Path, name + ".svg");
					if (File.Exists(svg))
						return svg;
				}
			}

			string? best = null;
			var bestDistance = int.MaxValue;

			foreach (var directory in index.Directories)
			{
				var matches = directory.Matches(size, scale);
				var distance = matches ? 0 : directory.Distance(size, scale);
				if (!matches && distance >= bestDistance)
					continue;

				var file = FindFile(theme, directory.Path, name);
				if (file is null)
					continue;

				if (matches)
					return file;

				best = file;
				bestDistance = distance;
			}

			return best;
		}

		private string? FindFile(string theme, string subDirectory, string name)
		{
			foreach (var baseDir in _options.IconDirectories)
			{
				var dir = Path.Combine(baseDir, theme, subDirectory);
				foreach (var ext in Extensions)
				{
					var file = Path.Combine(dir, name + ext);
					if (File.Exists(file))
						return file;
				}
			}

			return null;
		}

		private IconLookupResult? FindFallback(string name)
		{
			foreach (var dir in _options.IconDirectories.Concat(_options.PixmapDirectories))
			{
				foreach (var ext in Extensions)
				{
					var file = Path.Combine(dir, name + ext);
					if (File.Exists(file))
						return ToResult(file);
				}
			}

			return null;
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
