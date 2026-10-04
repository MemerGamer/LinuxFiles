// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Mime;
using Files.Platform.Linux.Launching;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Mime
{
	/// <summary>
	/// Resolves applications and default associations from Desktop Entry files, mimeapps.list and mimeinfo.cache.
	/// </summary>
	public sealed class LinuxApplicationRegistry : IApplicationRegistry
	{
		private readonly XdgDirectories xdg;
		private readonly CultureInfo culture;
		private readonly IExecutableLocator locator;

		/// <summary>
		/// Creates the registry for the process environment.
		/// </summary>
		public LinuxApplicationRegistry() : this(XdgDirectories.FromEnvironment(), CultureInfo.CurrentUICulture, new PathExecutableLocator())
		{
		}

		/// <summary>
		/// Creates the registry for explicit directories, culture and executable lookup.
		/// </summary>
		public LinuxApplicationRegistry(XdgDirectories xdg, CultureInfo culture, IExecutableLocator locator)
		{
			this.xdg = xdg;
			this.culture = culture;
			this.locator = locator;
		}

		/// <inheritdoc/>
		public Task<IReadOnlyList<DesktopApplication>> GetApplicationsForMimeTypeAsync(string mimeType, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var (_, all) = BuildCandidates(mimeType);
			var apps = new List<DesktopApplication>();
			foreach (var id in all)
			{
				var app = Load(id);
				if (app is not null && !app.NoDisplay)
					apps.Add(app);
			}

			return Task.FromResult<IReadOnlyList<DesktopApplication>>(apps);
		}

		/// <inheritdoc/>
		public Task<DesktopApplication?> GetDefaultApplicationAsync(string mimeType, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var (defaults, all) = BuildCandidates(mimeType);
			foreach (var id in defaults)
			{
				var app = Load(id);
				if (app is not null)
					return Task.FromResult<DesktopApplication?>(app);
			}

			// No explicit default: fall back to the first visible association, like xdg-open
			foreach (var id in all)
			{
				var app = Load(id);
				if (app is not null && !app.NoDisplay)
					return Task.FromResult<DesktopApplication?>(app);
			}

			return Task.FromResult<DesktopApplication?>(null);
		}

		/// <inheritdoc/>
		public Task<DesktopApplication?> GetApplicationAsync(string desktopId, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(Load(desktopId));
		}

		/// <inheritdoc/>
		public async Task SetDefaultApplicationAsync(string mimeType, string desktopId, CancellationToken cancellationToken = default)
		{
			var path = Path.Combine(xdg.ConfigHome, "mimeapps.list");
			string[] existing;
			try
			{
				existing = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
			}
			catch (FileNotFoundException)
			{
				existing = [];
			}
			catch (DirectoryNotFoundException)
			{
				existing = [];
			}

			var lines = MimeAppsList.SetDefault(existing, mimeType, desktopId);
			Directory.CreateDirectory(xdg.ConfigHome);

			var temp = path + ".tmp" + Environment.ProcessId;
			await File.WriteAllTextAsync(temp, string.Join('\n', lines) + "\n", cancellationToken).ConfigureAwait(false);
			File.Move(temp, path, overwrite: true);
		}

		private IEnumerable<string> MimeAppsListFiles()
		{
			var names = xdg.CurrentDesktops.Select(d => $"{d}-mimeapps.list").Append("mimeapps.list").ToList();

			foreach (var name in names)
				yield return Path.Combine(xdg.ConfigHome, name);

			foreach (var dir in xdg.ConfigDirs)
			{
				foreach (var name in names)
					yield return Path.Combine(dir, name);
			}

			foreach (var dir in xdg.AllDataDirs)
			{
				foreach (var name in names)
					yield return Path.Combine(dir, "applications", name);
			}
		}

		/// <summary>
		/// Returns the explicit defaults and the complete ordered candidate list for a MIME type.
		/// </summary>
		private (List<string> Defaults, List<string> All) BuildCandidates(string mimeType)
		{
			var defaults = new List<string>();
			var all = new List<string>();
			var removed = new HashSet<string>(StringComparer.Ordinal);

			void Add(List<string> list, string id)
			{
				if (!removed.Contains(id) && !list.Contains(id))
					list.Add(id);
			}

			foreach (var file in MimeAppsListFiles())
			{
				var list = MimeAppsList.Load(file);
				if (list is null)
					continue;

				if (list.Defaults.TryGetValue(mimeType, out var d))
				{
					foreach (var id in d)
					{
						Add(defaults, id);
						Add(all, id);
					}
				}

				if (list.Added.TryGetValue(mimeType, out var a))
				{
					foreach (var id in a)
						Add(all, id);
				}

				if (list.Removed.TryGetValue(mimeType, out var r))
				{
					foreach (var id in r)
						removed.Add(id);
				}
			}

			foreach (var id in ReadMimeInfoCache(mimeType))
				Add(all, id);

			return (defaults, all);
		}

		private IEnumerable<string> ReadMimeInfoCache(string mimeType)
		{
			var prefix = mimeType + "=";
			foreach (var dir in xdg.AllDataDirs)
			{
				var applications = Path.Combine(dir, "applications");
				var cache = Path.Combine(applications, "mimeinfo.cache");

				if (File.Exists(cache))
				{
					string[] lines;
					try
					{
						lines = File.ReadAllLines(cache);
					}
					catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
					{
						continue;
					}

					var line = lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
					if (line is not null)
					{
						foreach (var id in DesktopEntryParser.SplitList(line[prefix.Length..]))
							yield return id;
					}
				}
				else if (Directory.Exists(applications))
				{
					// Cache not generated: scan the desktop files directly
					foreach (var (id, file) in EnumerateDesktopFiles(applications))
					{
						var entry = DesktopEntryParser.ParseFile(file, id, culture);
						if (entry?.Application.MimeTypes?.Contains(mimeType) == true)
							yield return id;
					}
				}
			}
		}

		private static IEnumerable<(string Id, string Path)> EnumerateDesktopFiles(string applicationsDir)
		{
			IEnumerable<string> files;
			try
			{
				files = Directory.EnumerateFiles(applicationsDir, "*.desktop", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal).ToList();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				yield break;
			}

			foreach (var file in files)
				yield return (System.IO.Path.GetRelativePath(applicationsDir, file).Replace('/', '-'), file);
		}

		private DesktopApplication? Load(string desktopId)
		{
			foreach (var dir in xdg.AllDataDirs)
			{
				var applications = Path.Combine(dir, "applications");
				foreach (var candidate in CandidatePaths(applications, desktopId))
				{
					if (!File.Exists(candidate))
						continue;

					var entry = DesktopEntryParser.ParseFile(candidate, desktopId, culture);
					if (entry is null || entry.Hidden)
						return null;

					if (!string.IsNullOrEmpty(entry.TryExec) && locator.Locate(entry.TryExec) is null)
						return null;

					return entry.Application;
				}
			}

			return null;
		}

		private static IEnumerable<string> CandidatePaths(string applicationsDir, string desktopId)
		{
			if (desktopId.Contains('/') || desktopId.Contains(".."))
				yield break;

			yield return Path.Combine(applicationsDir, desktopId);

			// "vendor-app.desktop" may live in "vendor/app.desktop"
			var index = desktopId.IndexOf('-');
			while (index >= 0)
			{
				yield return Path.Combine(applicationsDir, desktopId[..index], desktopId[(index + 1)..]);
				index = desktopId.IndexOf('-', index + 1);
			}
		}
	}
}
