// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Win32.UI.Shell;

namespace Files.App.Data.Items
{
	/// <summary>
	/// Represents a recently used file.
	/// </summary>
	public sealed partial class RecentItem : WidgetCardItem, IEquatable<RecentItem>, IDisposable
	{
		private BitmapImage? _Icon;
		/// <summary>
		/// Gets or sets thumbnail icon of the recent item.
		/// </summary>
		public BitmapImage? Icon
		{
			get => _Icon;
			set => SetProperty(ref _Icon, value);
		}

		/// <summary>
		/// Gets or sets name of the recent item.
		/// </summary>
		public required string Name { get; set; }

		/// <summary>
		/// Gets or sets target path of the recent item.
		/// </summary>
		public required DateTime LastModified { get; set; }

		/// <summary>
		/// Gets or initializes the Windows shell item of the recent item; unused on Linux.
		/// </summary>
		public IShellItem? ShellItem { get; init; }

		/// <summary>
		/// Loads thumbnail icon of the recent item.
		/// </summary>
		/// <returns></returns>
		public async Task LoadRecentItemIconAsync()
		{
			var result = await FileThumbnailHelper.GetIconAsync(Path, Constants.ShellIconSizes.Small, false, IconOptions.None);

			await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
			{
				var bitmapImage = await result.ToBitmapAsync();
				if (bitmapImage is not null)
					Icon = bitmapImage;
			});
		}

		public override int GetHashCode() => (Path, Name).GetHashCode();
		public override bool Equals(object? other) => other is RecentItem item && Equals(item);
		public bool Equals(RecentItem? other) => other is not null && other.Name == Name && other.Path == Path;

		public void Dispose()
		{
		}
	}
}
