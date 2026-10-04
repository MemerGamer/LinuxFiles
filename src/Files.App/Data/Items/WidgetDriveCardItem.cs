// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Media.Imaging;

namespace Files.App.Data.Items
{
	public sealed partial class WidgetDriveCardItem : WidgetCardItem, IWidgetCardItem<DriveItem>, IComparable<WidgetDriveCardItem>
	{
		public new DriveItem Item { get; private set; }

		private BitmapImage? thumbnail;
		public BitmapImage? Thumbnail
		{
			get => thumbnail;
			set => SetProperty(ref thumbnail, value);
		}

		public WidgetDriveCardItem(DriveItem item)
		{
			Item = item;
			Path = item.Path;
		}

		public async Task LoadCardThumbnailAsync()
		{
#if !WINDOWS
			var result = await DriveHelpers.GetDriveIconAsync(Item.Type, Constants.ShellIconSizes.Large);
			if (result is null)
				return;
#else
			var result = await FileThumbnailHelper.GetIconAsync(
				Item.Path,
				Constants.ShellIconSizes.Large,
				true,
				IconOptions.ReturnIconOnly);

			if (result is null)
			{
				using var thumbnail = await DriveHelpers.GetThumbnailAsync(Item.Root!);
				result ??= await thumbnail.ToByteArrayAsync();
			}
#endif

			var bitmapImage = await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() => result.ToBitmapAsync(), Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal);
			if (bitmapImage is not null)
				Thumbnail = bitmapImage;
		}

		public int CompareTo(WidgetDriveCardItem? other)
			=> Item.Path!.CompareTo(other?.Item?.Path);
	}
}
