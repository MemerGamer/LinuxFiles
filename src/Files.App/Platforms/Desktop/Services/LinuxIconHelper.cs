// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions;
using Files.Platform.Abstractions.Icons;
using Files.Platform.Abstractions.Mime;
using Files.Platform.Abstractions.Thumbnails;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Resolves icon / thumbnail bytes on Linux: freedesktop thumbnails for files, otherwise the icon theme entry for the
	/// folder kind or MIME type. Replaces <c>Win32Helper.GetIcon</c> (shell icon extraction).
	/// </summary>
	internal static class LinuxIconHelper
	{
		private static readonly ConcurrentDictionary<(string Name, uint Size), byte[]?> _themeCache = new();

		private static IThumbnailService Thumbnails => Ioc.Default.GetRequiredService<IThumbnailService>();
		private static IIconThemeProvider Theme => Ioc.Default.GetRequiredService<IIconThemeProvider>();
		private static IMimeTypeService Mime => Ioc.Default.GetRequiredService<IMimeTypeService>();
		private static IUserDirectories Directories => Ioc.Default.GetRequiredService<IUserDirectories>();

		public static async Task<byte[]?> GetIconAsync(string? path, uint size, bool isFolder, IconOptions options)
		{
			try
			{
				if (!string.IsNullOrEmpty(path) && !isFolder && !options.HasFlag(IconOptions.ReturnIconOnly))
				{
					var thumbnail = await Thumbnails.GetThumbnailAsync(
						path,
						size,
						options.HasFlag(IconOptions.ReturnOnlyIfCached) ? ThumbnailOptions.ReturnOnlyIfCached : ThumbnailOptions.None);

					if (thumbnail is not null)
						return thumbnail;
				}

				if (options.HasFlag(IconOptions.ReturnOnlyIfCached))
					return null;

				var names = await GetIconNamesAsync(path, isFolder);
				var key = (names[0], size);
				if (_themeCache.TryGetValue(key, out var cached))
					return cached;

				var bytes = await LoadThemeIconAsync(names, size);
				_themeCache[key] = bytes;
				return bytes;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				App.Logger?.LogDebug(ex, "Icon lookup failed");
				return null;
			}
		}

		private static async Task<IReadOnlyList<string>> GetIconNamesAsync(string? path, bool isFolder)
		{
			if (isFolder || string.IsNullOrEmpty(path))
			{
				var special = GetSpecialFolderIconName(path);
				return special is null ? ["folder"] : [special, "folder"];
			}

			var mimeType = await Mime.GetMimeTypeAsync(path);
			var iconName = await Mime.GetIconNameAsync(mimeType);
			var generic = await Mime.GetGenericIconNameAsync(mimeType);
			return [iconName, generic, "text-x-generic"];
		}

		private static string? GetSpecialFolderIconName(string? path)
		{
			if (string.IsNullOrEmpty(path))
				return null;

			var trimmed = path.TrimEnd('/');
			if (trimmed == Directories.Home.TrimEnd('/')) return "user-home";
			if (trimmed == Directories.Desktop.TrimEnd('/')) return "user-desktop";
			if (trimmed == Directories.Documents.TrimEnd('/')) return "folder-documents";
			if (trimmed == Directories.Downloads.TrimEnd('/')) return "folder-download";
			if (trimmed == Directories.Music.TrimEnd('/')) return "folder-music";
			if (trimmed == Directories.Pictures.TrimEnd('/')) return "folder-pictures";
			if (trimmed == Directories.Videos.TrimEnd('/')) return "folder-videos";
			if (trimmed == Directories.PublicShare.TrimEnd('/')) return "folder-publicshare";
			if (trimmed == Directories.Templates.TrimEnd('/')) return "folder-templates";

			return null;
		}

		private static async Task<byte[]?> LoadThemeIconAsync(IReadOnlyList<string> names, uint size)
		{
			var result = await Theme.ResolveIconAsync(names, size);
			if (result is null)
				return null;

			// LINUX-TODO(icons): rasterize SVG theme icons (needs an SVG renderer); PNG theme entries work already
			if (result.Value.IsSvg)
				return null;

			return await File.ReadAllBytesAsync(result.Value.Path);
		}
	}
}
