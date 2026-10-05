// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace Files.App.Data.Items
{
	public sealed partial class WidgetFolderCardItem : WidgetCardItem, IWidgetCardItem<IWindowsStorable>, IDisposable
	{
		// Properties

		public string? AutomationProperties { get; set; }

		public new IWindowsStorable Item { get; private set; }

		public string? Text { get; set; }

		public bool IsPinned { get; set; }

		public string Tooltip { get; set; }

		private BitmapImage? _Thumbnail;
		public BitmapImage? Thumbnail { get => _Thumbnail; set => SetProperty(ref _Thumbnail, value); }

		// Constructor

		public WidgetFolderCardItem(IWindowsStorable item, string text, bool isPinned, string tooltip)
		{
			AutomationProperties = text;
			Item = item;
			Text = text;
			IsPinned = isPinned;
			Path = item.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING);
			Tooltip = tooltip;
		}

#if !WINDOWS
		/// <summary>
		/// Creates a card for a plain folder path; there is no shell item behind it on Linux.
		/// </summary>
		public WidgetFolderCardItem(string path, string text, bool isPinned, string tooltip)
		{
			AutomationProperties = text;
			Item = null!;
			Text = text;
			IsPinned = isPinned;
			Path = path;
			Tooltip = tooltip;
		}
#endif

		// Methods

		public async Task LoadCardThumbnailAsync()
		{
			if (string.IsNullOrEmpty(Path))
				return;

			if (Item is null)
			{
				byte[]? icon = Path == Constants.UserEnvironmentPaths.RecycleBinPath
					? await DriveHelpers.GetDriveIconAsync(null, Constants.ShellIconSizes.Large)
					: await FileThumbnailHelper.GetIconAsync(Path, (uint)Constants.ShellIconSizes.Large, true, IconOptions.ReturnIconOnly);
				if (icon is not null)
					Thumbnail = await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() => icon.ToBitmapAsync(), Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal);
				return;
			}

			var thumbnailSize = (int)(Constants.ShellIconSizes.Large * App.AppModel.AppWindowDPI);
			// Ensure thumbnail size is at least 1 to prevent layout errors
			thumbnailSize = Math.Max(1, thumbnailSize);
			Item.TryGetThumbnail(thumbnailSize, SIIGBF.SIIGBF_ICONONLY, out var rawThumbnailData);
			if (rawThumbnailData is null)
				return;

			Thumbnail = await rawThumbnailData.ToBitmapAsync();
		}

		public void Dispose()
		{
			Item?.Dispose();
		}
	}
}
