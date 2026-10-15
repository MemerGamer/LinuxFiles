// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Previews;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.App.UserControls.FilePreviews
{
	public sealed partial class NativePreviewFallback : UserControl
	{
		public BasePreviewModel Model { get; }

		public NativePreviewFallback(BasePreviewModel model, UserControl preview, bool showMetadata = false)
		{
			Model = model;
			InitializeComponent();
			PreviewContent.Content = preview;
			Metadata.Visibility = showMetadata ? Visibility.Visible : Visibility.Collapsed;
			if (!showMetadata)
				OpenButton.Content = Strings.OpenWithDefaultApp.GetLocalizedResource();
			Unloaded += Model.PreviewControlBase_Unloaded;
		}

		internal static async Task<UserControl> CreateHtmlAsync(ListedItem item, CancellationToken token)
		{
			var model = new TextPreviewViewModel(item) { Kind = TextPreviewKind.Plain };
			using var registration = token.Register(model.LoadCancelledTokenSource.Cancel);
			await model.LoadAsync();
			token.ThrowIfCancellationRequested();
			return new NativePreviewFallback(model, new DesktopTextPreview(model));
		}

		internal static async Task<UserControl> CreateMediaAsync(ListedItem item, CancellationToken token)
		{
			var model = new MediaMetadataPreviewViewModel(item);
			using var registration = token.Register(model.LoadCancelledTokenSource.Cancel);
			await model.LoadAsync();
			token.ThrowIfCancellationRequested();
			return new NativePreviewFallback(model, new BasicPreview(model), showMetadata: true);
		}

		private async void Open_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				await NavigationHelpers.OpenPreviewFileAsync(Model.Item.GetRequiredPath());
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Failed to open preview file");
			}
		}
	}
}
