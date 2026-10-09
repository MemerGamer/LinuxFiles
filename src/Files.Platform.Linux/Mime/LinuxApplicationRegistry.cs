// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Mime;
using Files.Platform.Abstractions.Diagnostics;
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
		private readonly MimeHierarchy hierarchy;
		private readonly ParsedFileCache<DesktopEntryParser.Entry> desktopEntries = new();
		private readonly ParsedFileCache<MimeAppsList> associations = new();
		private readonly ParsedFileCache<Dictionary<string, string[]>> mimeInfo = new();

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
			hierarchy = new MimeHierarchy(xdg);
		}

		/// <inheritdoc/>
		public Task<IReadOnlyList<DesktopApplication>> GetApplicationsForMimeTypeAsync(string mimeType, CancellationToken cancellationToken = default)
		{
			return Task.Run<IReadOnlyList<DesktopApplication>>(() =>
			{
				using var trace = PerformanceTrace.Begin("open-with-query", false);
				cancellationToken.ThrowIfCancellationRequested();

				var apps = new List<DesktopApplication>();
				var seen = new HashSet<string>(StringComparer.Ordinal);
				foreach (var type in hierarchy.GetChain(mimeType))
				{
					var (_, all) = BuildCandidates(type);
					foreach (var id in all)
					{
						if (!seen.Add(id))
							continue;

						var app = Load(id);
						if (app is not null && !app.NoDisplay)
							apps.Add(app);
					}
				}

				return (IReadOnlyList<DesktopApplication>)apps;
			}, cancellationToken);
		}

		/// <inheritdoc/>
		public Task<IReadOnlyList<DesktopApplication>> GetAllApplicationsAsync(CancellationToken cancellationToken = default)
		{
			return Task.Run<IReadOnlyList<DesktopApplication>>(() =>
			{
				using var trace = PerformanceTrace.Begin("open-with-query", false);
				var apps = new List<DesktopApplication>();
				var seen = new HashSet<string>(StringComparer.Ordinal);
				foreach (var dir in xdg.AllDataDirs)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var applications = Path.Combine(dir, "applications");
					if (!Directory.Exists(applications))
						continue;

					foreach (var (id, _) in EnumerateDesktopFiles(applications))
					{
						if (!seen.Add(id))
							continue;

						var app = Load(id);
						if (app is not null && !app.NoDisplay)
							apps.Add(app);
					}
				}

				apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
				return (IReadOnlyList<DesktopApplication>)apps;
			}, cancellationToken);
		}

		/// <inheritdoc/>
		public Task<DesktopApplication?> GetDefaultApplicationAsync(string mimeType, CancellationToken cancellationToken = default)
		{
			return Task.Run<DesktopApplication?>(() =>
			{
				using var trace = PerformanceTrace.Begin("open-with-query", false);
				cancellationToken.ThrowIfCancellationRequested();

				// Per type, nearest first: the explicit default, else the first visible association (like xdg-open)
				foreach (var type in hierarchy.GetChain(mimeType))
				{
					var (defaults, all) = BuildCandidates(type);
					foreach (var id in defaults)
					{
						var app = Load(id);
						if (app is not null)
							return app;
					}

					foreach (var id in all)
					{
						var app = Load(id);
						if (app is not null && !app.NoDisplay)
							return app;
					}
				}

				return (DesktopApplication?)null;
			}, cancellationToken);
		}

		/// <inheritdoc/>
		public Task<DesktopApplication?> GetApplicationAsync(string desktopId, CancellationToken cancellationToken = default)
		{
			return Task.Run<DesktopApplication?>(() =>
			{
				using var trace = PerformanceTrace.Begin("open-with-query", false);
				cancellationToken.ThrowIfCancellationRequested();
				return Load(desktopId);
			}, cancellationToken);
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
				var list = associations.Get(file, MimeAppsList.Load);
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
			foreach (var dir in xdg.AllDataDirs)
			{
				var applications = Path.Combine(dir, "applications");
				var cache = Path.Combine(applications, "mimeinfo.cache");
				if (File.Exists(cache))
				{
					var mapping = mimeInfo.Get(cache, ReadMimeInfo);
					if (mapping?.TryGetValue(mimeType, out var ids) == true)
						foreach (var id in ids) yield return id;
				}
				else if (Directory.Exists(applications))
				{
					foreach (var (id, file) in EnumerateDesktopFiles(applications))
					{
						var entry = desktopEntries.Get(file, p => DesktopEntryParser.ParseFile(p, id, culture));
						if (entry?.Application.MimeTypes?.Contains(mimeType) == true)
							yield return id;
					}
				}
			}
		}

		private static Dictionary<string, string[]>? ReadMimeInfo(string path)
		{
			try
			{
				var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
				foreach (var line in File.ReadLines(path))
				{
					var eq = line.IndexOf('=');
					if (eq > 0)
						result.TryAdd(line[..eq], DesktopEntryParser.SplitList(line[(eq + 1)..]));
				}
				return result;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return null;
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

					var entry = desktopEntries.Get(candidate, p => DesktopEntryParser.ParseFile(p, desktopId, culture));
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
