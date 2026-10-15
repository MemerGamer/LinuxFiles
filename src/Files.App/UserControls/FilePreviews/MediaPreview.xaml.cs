using Files.App.ViewModels.Previews;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Input;
using Windows.Media.Playback;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Files.App.UserControls.FilePreviews
{
	public sealed partial class MediaPreview : UserControl
	{
		private IUserSettingsService UserSettingsService { get; } = Ioc.Default.GetRequiredService<IUserSettingsService>();

		public bool NativeViewsAvailable { get; } = NativePreviewSupport.IsAvailable;

		public MediaPreview(MediaPreviewViewModel model)
		{
			ViewModel = model;
			InitializeComponent();
			if (NativeViewsAvailable)
				PlayerContext.Loaded += PlayerContext_Loaded;
			else
				Loaded += LoadFallback;
			Unloaded += MediaPreview_Unloaded;
		}

		public MediaPreviewViewModel ViewModel { get; set; }

		private async void LoadFallback(object sender, RoutedEventArgs e)
		{
			Loaded -= LoadFallback;
			try
			{
				FallbackContent.Content = await NativePreviewFallback.CreateMediaAsync(ViewModel.Item, ViewModel.LoadCancelledTokenSource.Token);
			}
			catch (OperationCanceledException) when (ViewModel.LoadCancelledTokenSource.IsCancellationRequested) { }
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Failed to load preview fallback");
			}
		}

		private void PlayerContext_Loaded(object sender, RoutedEventArgs e)
		{
			PlayerContext.MediaPlayer.Volume = UserSettingsService.InfoPaneSettingsService.MediaVolume;
			PlayerContext.MediaPlayer.VolumeChanged += MediaPlayer_VolumeChanged;
			ViewModel.TogglePlaybackRequested += TogglePlaybackRequestInvoked;
		}

		private void MediaPreview_Unloaded(object sender, RoutedEventArgs e)
		{
			if (!NativeViewsAvailable)
			{
				ViewModel.PreviewControlBase_Unloaded(sender, e);
				return;
			}

			// The MediaPlayerElement isn't properly disposed by Windows so we set the source to null
			// to avoid issues the next time the control is used.
			PlayerContext.Source = null;
			ViewModel.PreviewControlBase_Unloaded(sender, e);

			PlayerContext.Loaded -= PlayerContext_Loaded;
			Unloaded -= MediaPreview_Unloaded;

			PlayerContext.MediaPlayer.VolumeChanged -= MediaPlayer_VolumeChanged;
			ViewModel.TogglePlaybackRequested -= TogglePlaybackRequestInvoked;
		}

		private void MediaPlayer_VolumeChanged(MediaPlayer sender, object args)
		{
			if (sender.Volume != UserSettingsService.InfoPaneSettingsService.MediaVolume)
			{
				UserSettingsService.InfoPaneSettingsService.MediaVolume = sender.Volume;
			}
		}

		private void TogglePlaybackRequestInvoked(object? sender, EventArgs e)
		{
			if (!NativeViewsAvailable)
				return;

			if (PlayerContext.MediaPlayer.PlaybackSession.PlaybackState is not MediaPlaybackState.Playing)
			{
				PlayerContext.MediaPlayer.Play();
			}
			else
			{
				PlayerContext.MediaPlayer.Pause();
			}
		}

		private void TogglePlaybackAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
		{
			TogglePlaybackRequestInvoked(sender, EventArgs.Empty);
		}
	}
}
