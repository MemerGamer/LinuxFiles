// Copyright (c) Files Community
// Licensed under the MIT License.

#if DESKTOP
using Files.App.ViewModels.Previews;
using Files.Platform.Linux.Previews;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Files.App.UserControls.FilePreviews
{
	public sealed partial class LinuxHtmlPreview : UserControl
	{
		private readonly CancellationTokenSource lifetime;
		private CancellationTokenSource? rendering;
		private XamlRoot? previewRoot;
		private int requestedWidth;
		private int snapshotWidth;
		private int requestedScale;
		private bool loaded;
		private bool failed;
		private bool unloaded;

		public HtmlPreviewViewModel ViewModel { get; }

		public LinuxHtmlPreview(HtmlPreviewViewModel viewModel, CancellationToken token)
		{
			ViewModel = viewModel;
			lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, ViewModel.LoadCancelledTokenSource.Token);
			InitializeComponent();
			Loaded += (_, _) =>
			{
				loaded = true;
				previewRoot = XamlRoot;
				if (previewRoot is not null) previewRoot.Changed += Root_Changed;
				UpdateSize();
			};
			Unloaded += Preview_Unloaded;
		}

		private void Preview_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSize();
		private void Root_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateSize();

		private void UpdateSize()
		{
			if (!loaded || unloaded || failed || lifetime.IsCancellationRequested || PreviewHost.ActualWidth < 1) return;
			Snapshot.Width = Math.Min(snapshotWidth == 0 ? PreviewHost.ActualWidth : snapshotWidth, PreviewHost.ActualWidth);
			var width = (int)Math.Clamp(PreviewHost.ActualWidth, 1, 2048);
			var scale = (int)Math.Clamp(Math.Ceiling(XamlRoot?.RasterizationScale ?? 1), 1, 2);
			if (requestedWidth != 0 && Math.Abs(width - requestedWidth) <= requestedWidth * 0.1 && scale == requestedScale) return;
			requestedWidth = width;
			requestedScale = scale;
			rendering?.Cancel();
			rendering = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
			_ = RenderAsync(width, scale, rendering);
		}

		private async Task RenderAsync(int width, int scale, CancellationTokenSource request)
		{
			var token = request.Token;
			try
			{
				await Task.Delay(300, token);
				Loading.IsActive = true;
				Loading.Visibility = Visibility.Visible;
				var png = await HtmlSnapshotRenderer.RenderAsync(ViewModel.Item.GetRequiredPath(), width, scale, token);
				token.ThrowIfCancellationRequested();
				if (png is null)
				{
					await ShowFallbackAsync(token);
				}
				else
				{
					using var stream = new MemoryStream(png);
					using var random = stream.AsRandomAccessStream();
					var image = new BitmapImage();
					await image.SetSourceAsync(random);
					token.ThrowIfCancellationRequested();
					snapshotWidth = width;
					Snapshot.Width = Math.Min(width, PreviewHost.ActualWidth);
					Snapshot.Source = image;
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Failed to display HTML snapshot");
				if (!token.IsCancellationRequested && !failed)
				{
					try
					{
						await ShowFallbackAsync(token);
					}
					catch (OperationCanceledException) { }
					catch (Exception fallbackError) { App.Logger.LogWarning(fallbackError, "Failed to load HTML source fallback"); }
				}
			}
			finally
			{
				if (ReferenceEquals(rendering, request))
				{
					rendering = null;
					Loading.IsActive = false;
					Loading.Visibility = Visibility.Collapsed;
				}
				request.Dispose();
			}
		}

		private async Task ShowFallbackAsync(CancellationToken token)
		{
			var fallback = await NativePreviewFallback.CreateHtmlAsync(ViewModel.Item, token);
			token.ThrowIfCancellationRequested();
			failed = true;
			Snapshot.Source = null;
			SnapshotScroll.Visibility = OpenButton.Visibility = Visibility.Collapsed;
			Fallback.Content = fallback;
			Fallback.Visibility = Visibility.Visible;
		}

		private async void Open_Click(object sender, RoutedEventArgs e)
		{
			try { await NavigationHelpers.OpenPreviewFileAsync(ViewModel.Item.GetRequiredPath()); }
			catch (Exception ex) { App.Logger.LogWarning(ex, "Failed to open preview file"); }
		}

		private void Preview_Unloaded(object sender, RoutedEventArgs e)
		{
			if (unloaded) return;
			unloaded = true;
			if (previewRoot is not null) previewRoot.Changed -= Root_Changed;
			lifetime.Cancel();
			lifetime.Dispose();
			Snapshot.Source = null;
			ViewModel.PreviewControlBase_Unloaded(sender, e);
		}
	}
}
#endif
