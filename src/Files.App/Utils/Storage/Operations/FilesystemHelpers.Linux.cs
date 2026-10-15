// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Clipboard;
using Microsoft.Extensions.Logging;
using System.IO;
using Windows.ApplicationModel.DataTransfer;

namespace Files.App.Utils.Storage
{
	public sealed partial class FilesystemHelpers
	{
		public static bool HasDraggedStorageItems(DataPackageView packageView)
			=> packageView is not null && (packageView.Contains(StandardDataFormats.StorageItems) || packageView.Contains(ClipboardFormats.UriList));

		public static async Task<IEnumerable<IStorageItemWithPath>> GetDraggedStorageItems(DataPackageView packageView)
		{
			try
			{
				IEnumerable<string> paths;
				if (packageView.Contains(ClipboardFormats.UriList))
				{
					// Uno's X11 host exposes the list as text, the Wayland host as the raw bytes it received.
					paths = await packageView.GetDataAsync(ClipboardFormats.UriList) switch
					{
						string text => ClipboardFormats.ParseUriList(text),
						byte[] bytes => ClipboardFormats.ParseUriList(System.Text.Encoding.UTF8.GetString(bytes)),
						_ => [],
					};
				}
				else if (packageView.Contains(StandardDataFormats.StorageItems))
				{
					// Uno exposes inbound XDND paths through this transport; keep only the paths.
					paths = (await packageView.GetStorageItemsAsync()).Select(item => item.Path);
				}
				else
				{
					return [];
				}

				return paths.Where(Path.IsPathFullyQualified).Distinct(StringComparer.Ordinal)
					.Select(path => new StorableWithPath(path, Directory.Exists(path) ? FilesystemItemType.Directory : FilesystemItemType.File))
					.ToList();
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Could not read dragged file paths.");
				return [];
			}
		}
	}
}
