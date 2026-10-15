using Files.App.ViewModels.Previews;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.Logging;
using System.IO;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Files.App.UserControls.FilePreviews
{
	public sealed partial class HtmlPreview : UserControl
	{
		public bool NativeViewsAvailable { get; } = NativePreviewSupport.IsAvailable;

		public HtmlPreview(HtmlPreviewViewModel model)
		{
			ViewModel = model;
			InitializeComponent();
			if (!NativeViewsAvailable)
			{
				Loaded += LoadFallback;
				Unloaded += ViewModel.PreviewControlBase_Unloaded;
			}
		}

		public HtmlPreviewViewModel ViewModel { get; set; }

		private async void LoadFallback(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
		{
			Loaded -= LoadFallback;
			try
			{
				FallbackContent.Content = await NativePreviewFallback.CreateHtmlAsync(ViewModel.Item, ViewModel.LoadCancelledTokenSource.Token);
			}
			catch (OperationCanceledException) when (ViewModel.LoadCancelledTokenSource.IsCancellationRequested) { }
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Failed to load preview fallback");
			}
		}

		private async void WebViewControl_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
		{
			await WebViewControl.EnsureCoreWebView2Async();
			WebViewControl.CoreWebView2.SetVirtualHostNameToFolderMapping(
				"preview.files",
				Path.GetDirectoryName(ViewModel.Item.ItemPath),
				Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.DenyCors);
			WebViewControl.Source = new Uri("http://preview.files/" + ViewModel.Item.Name);
		}
	}
}