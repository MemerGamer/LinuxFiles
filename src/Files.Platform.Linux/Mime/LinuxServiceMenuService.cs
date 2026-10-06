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
	public sealed class LinuxServiceMenuService : IServiceMenuService
	{
		public const int MaxScannedFiles = 512;
		public const int MaxActions = 256;
		public const int MaxSelection = 256;
		private readonly XdgDirectories directories;
		private readonly IMimeTypeService mimeTypes;
		private readonly CultureInfo culture;
		private readonly MimeHierarchy hierarchy;

		public LinuxServiceMenuService(XdgDirectories directories, IMimeTypeService mimeTypes, CultureInfo culture)
		{
			this.directories = directories;
			this.mimeTypes = mimeTypes;
			this.culture = culture;
			hierarchy = new MimeHierarchy(directories);
		}

		public async Task<IReadOnlyList<ServiceMenuAction>> GetActionsAsync(IReadOnlyList<string> targets, CancellationToken cancellationToken = default)
		{
			if (targets.Count == 0 || targets.Count > MaxSelection)
				return [];
			var types = new List<string>();
			foreach (var target in targets)
				types.Add(await mimeTypes.GetMimeTypeAsync(DesktopExecExpander.ToPathOrUri(target), cancellationToken).ConfigureAwait(false));
			return await Task.Run(() => Scan(targets, types, cancellationToken), cancellationToken).ConfigureAwait(false);
		}

		private IReadOnlyList<ServiceMenuAction> Scan(IReadOnlyList<string> targets, IReadOnlyList<string> types, CancellationToken cancellationToken)
		{
			var result = new List<ServiceMenuAction>();
			var seen = new HashSet<string>(StringComparer.Ordinal);
			var scanned = 0;
			// User overrides precede system entries, including hidden or invalid overrides.
			foreach (var data in new[] { directories.DataHome, Path.Combine(directories.Home, ".local", "share") }.Concat(directories.DataDirs).Distinct(StringComparer.Ordinal))
			{
				foreach (var relative in new[] { "kio/servicemenus", "kservices5/ServiceMenus" })
				{
					try
					{
						var dir = Path.Combine(data, relative);
						if (!Directory.Exists(dir)) continue;
						foreach (var path in Directory.EnumerateFileSystemEntries(dir))
						{
							cancellationToken.ThrowIfCancellationRequested();
							if (++scanned > MaxScannedFiles || result.Count >= MaxActions)
								return Ordered(result);
							if (!path.EndsWith(".desktop", StringComparison.Ordinal) || !seen.Add(Path.GetFileName(path))) continue;
							var menu = Read(path, culture, out _);
							if (menu is null || !menu.Matches(targets, types, hierarchy)) continue;
							result.AddRange(menu.Actions.Take(MaxActions - result.Count));
						}
					}
					catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
					{
					}
				}
			}
			return Ordered(result);
		}

		private static IReadOnlyList<ServiceMenuAction> Ordered(List<ServiceMenuAction> actions) => actions
			.OrderBy(a => a.Priority == "TopLevel" ? 0 : a.Priority == "Important" ? 1 : 2)
			.ThenBy(a => a.Submenu ?? a.Application.Name, StringComparer.CurrentCulture)
			.ToArray();

		internal static ServiceMenuEntry? Read(string path, CultureInfo culture, out FileIdentity? identity)
		{
			identity = FileIdentity.TryCapture(path);
			if (identity is null) return null;
			try
			{
				var lines = DesktopEntryDisplay.ReadLinesBounded(path);
				if (lines is null || lines.Count(l => l.Contains('=')) > DesktopEntryDisplay.MaxKeys) return null;
				var menu = ServiceMenuParser.ParseStrict(lines, path, culture);
				return identity.Value.StillMatches(path) ? menu : null;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
			{
				return null;
			}
		}
	}
}
