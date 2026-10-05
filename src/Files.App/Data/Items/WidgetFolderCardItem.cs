// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Media.Imaging;

namespace Files.App.Data.Items
{
	public sealed partial class WidgetFolderCardItem : WidgetCardItem, IWidgetCardItem<IStorable>, IDisposable
	{
		// Properties

		public string? AutomationProperties { get; set; }

		public new IStorable Item { get; private set; }

		public string? Text { get; set; }

		public bool IsPinned { get; set; }

		public string Tooltip { get; set; }

		private BitmapImage? _Thumbnail;
		public BitmapImage? Thumbnail { get => _Thumbnail; set => SetProperty(ref _Thumbnail, value); }
		private bool _isDisposed;

		// Constructor

		public WidgetFolderCardItem(IStorable item, string text, string path, bool isPinned, string tooltip)
		{
			AutomationProperties = text;
			Item = item;
			Text = text;
			IsPinned = isPinned;
			Path = path;
			Tooltip = tooltip;
		}

#if !WINDOWS
		/// <summary>
		/// Creates a card for a plain folder path; there is no shell item behind it on Linux.
		/// </summary>
		public WidgetFolderCardItem(string path, string text, bool isPinned, string tooltip)
		{
			AutomationProperties = text;
			Item = SystemIO.Path.IsPathFullyQualified(path)
				? new OwlCore.Storage.System.IO.SystemFolder(path)
				: new FolderPath(path, text);
			Text = text;
			IsPinned = isPinned;
			Path = path;
			Tooltip = tooltip;
		}
#endif

		private sealed record FolderPath(string Id, string Name) : IStorable;

		// Methods

#if !WINDOWS
		public async Task LoadCardThumbnailAsync()
		{
			if (_isDisposed || string.IsNullOrEmpty(Path))
				return;

			byte[]? icon = Path == Constants.UserEnvironmentPaths.RecycleBinPath
				? await DriveHelpers.GetDriveIconAsync(null, Constants.ShellIconSizes.Large)
				: await FileThumbnailHelper.GetIconAsync(Path, (uint)Constants.ShellIconSizes.Large, true, IconOptions.ReturnIconOnly);
			if (_isDisposed || icon is null)
				return;

			await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
			{
				var thumbnail = await icon.ToBitmapAsync();
				if (!_isDisposed)
					Thumbnail = thumbnail;
			});
		}
#endif

		public void Dispose()
		{
			_isDisposed = true;
			(Item as IDisposable)?.Dispose();
		}
	}
}
