// Copyright (c) Files Community
// Licensed under the MIT License.

#if DESKTOP
using Files.App.ViewModels.Previews;
using Files.Platform.Linux.Media;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Files.App.UserControls.FilePreviews
{
	public sealed partial class LinuxMediaPreview : UserControl
	{
		private readonly IUserSettingsService settings = Ioc.Default.GetRequiredService<IUserSettingsService>();
		private readonly DispatcherQueue windowDispatcher;
		private readonly object frameGate = new();
		private readonly SemaphoreSlim commandGate = new(1, 1);
		private MpvMediaPlayer? player;
		private MpvFrame? latestFrame;
		private WriteableBitmap? bitmap;
		private bool uiPending;
		private bool updatingControls = true;
		private bool commandBusy;
		private bool failed;
		private int unloaded;
		private XamlRoot? previewRoot;

		public MediaMetadataPreviewViewModel ViewModel { get; }

		public LinuxMediaPreview(MediaMetadataPreviewViewModel viewModel)
		{
			ViewModel = viewModel;
			windowDispatcher = MainWindow.Instance.DispatcherQueue;
			InitializeComponent();
			VolumeSlider.Value = Math.Clamp(settings.InfoPaneSettingsService.MediaVolume, 0, 1);
			PlaybackTime.Text = FormatPlaybackTime(TimeSpan.Zero, TimeSpan.Zero);
			updatingControls = false;
			Loaded += (_, _) =>
			{
				previewRoot = XamlRoot;
				if (previewRoot is not null) previewRoot.Changed += Root_Changed;
				UpdateTargetSize();
			};
			Unloaded += Preview_Unloaded;
		}

		public async void TogglePlayback()
		{
			if (Volatile.Read(ref unloaded) != 0 || commandBusy || failed) return;
			commandBusy = true;
			try
			{
				if (player is null)
				{
					// Construction is cheap; native initialization and parsing start only on explicit Play.
					var created = new MpvMediaPlayer { Volume = VolumeSlider.Value };
					player = created;
					created.FrameRendered += Player_FrameRendered;
					created.StateChanged += Player_StateChanged;
					UpdateTargetSize();
					var path = ViewModel.Item.GetRequiredPath();
					await RunCommandAsync(() => { created.Open(path); created.Play(); });
				}
				else
				{
					var current = player;
					await RunCommandAsync(current.TogglePause);
				}
			}
			catch (Exception) { ShowFailure(); }
			finally { commandBusy = false; }
		}

		private async Task RunCommandAsync(Action command)
		{
			await commandGate.WaitAsync();
			try
			{
				if (Volatile.Read(ref unloaded) == 0) await Task.Run(command);
			}
			finally { commandGate.Release(); }
		}

		private void Play_Click(object sender, RoutedEventArgs e) => TogglePlayback();

		private void Player_StateChanged(object? sender, EventArgs e) => QueueUiUpdate();

		private void Player_FrameRendered(MpvFrame frame)
		{
			lock (frameGate)
			{
				if (Volatile.Read(ref unloaded) != 0) return;
				// Each frame owns its pixels. Replacing this reference drops obsolete frames safely.
				latestFrame = frame;
			}
			QueueUiUpdate();
		}

		private void QueueUiUpdate()
		{
			lock (frameGate)
			{
				if (Volatile.Read(ref unloaded) != 0 || uiPending) return;
				uiPending = true;
				if (!windowDispatcher.TryEnqueue(UpdateUi)) uiPending = false;
			}
		}

		private void UpdateUi()
		{
			MpvFrame? frame;
			lock (frameGate)
			{
				uiPending = false;
				frame = latestFrame;
				latestFrame = null;
			}
			if (Volatile.Read(ref unloaded) != 0 || failed || player is not { } current) return;
			if (current.Error is not null) { ShowFailure(); return; }
			try
			{
				if (frame is not null)
				{
					if (bitmap is null || bitmap.PixelWidth != frame.Width || bitmap.PixelHeight != frame.Height)
					{
						bitmap = new WriteableBitmap(frame.Width, frame.Height);
						Video.Source = bitmap;
					}
					using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(frame.Pixels.Span);
					bitmap.Invalidate();
					Video.Visibility = Visibility.Visible;
					Cover.Visibility = Visibility.Collapsed;
				}
				updatingControls = true;
				var duration = current.Duration.TotalSeconds;
				SeekSlider.Maximum = Math.Max(1, duration);
				SeekSlider.IsEnabled = duration > 0;
				SeekSlider.Value = Math.Clamp(current.Position.TotalSeconds, 0, SeekSlider.Maximum);
				PlaybackTime.Text = FormatPlaybackTime(current.Position, current.Duration);
				var label = (current.IsPaused ? Strings.Play : Strings.Key_Pause).GetLocalizedResource();
				PlayIcon.Glyph = current.IsPaused ? "\uE768" : "\uE769";
				AutomationProperties.SetName(PlayButton, label);
				ToolTipService.SetToolTip(PlayButton, label);
			}
			catch (Exception) { ShowFailure(); }
			finally { updatingControls = false; }
		}

		private async void Seek_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
		{
			if (updatingControls || Volatile.Read(ref unloaded) != 0 || player is not { } current) return;
			try { await RunCommandAsync(() => current.Seek(TimeSpan.FromSeconds(e.NewValue))); }
			catch (Exception) { ShowFailure(); }
		}

		private async void Volume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
		{
			if (updatingControls || Volatile.Read(ref unloaded) != 0) return;
			settings.InfoPaneSettingsService.MediaVolume = e.NewValue;
			if (failed) return;
			if (player is not { } current) return;
			try { await RunCommandAsync(() => current.Volume = e.NewValue); }
			catch (Exception) { ShowFailure(); }
		}

		private static string FormatPlaybackTime(TimeSpan position, TimeSpan duration) => string.Format(CultureInfo.CurrentCulture,
			Strings.LinuxMediaTimeFormat.GetLocalizedResource(), FormatTime(position), FormatTime(duration));

		private static string FormatTime(TimeSpan time) => time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.CurrentCulture);

		private void VideoHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTargetSize();

		private void Root_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateTargetSize();

		private void UpdateTargetSize()
		{
			var scale = XamlRoot?.RasterizationScale ?? 1;
			player?.SetTargetSize((int)Math.Clamp(VideoHost.ActualWidth * scale, 1, 1920),
				(int)Math.Clamp(VideoHost.ActualHeight * scale, 1, 1080));
		}

		private void ShowFailure()
		{
			if (Volatile.Read(ref unloaded) != 0) return;
			failed = true;
			Video.Visibility = Visibility.Collapsed;
			Video.Source = null;
			bitmap = null;
			Cover.Visibility = Visibility.Visible;
			PlaybackError.Visibility = Visibility.Visible;
			PlayIcon.Glyph = "\uE768";
			PlayButton.IsEnabled = SeekSlider.IsEnabled = false;
			player?.Dispose();
		}

		private void Preview_Unloaded(object sender, RoutedEventArgs e)
		{
			Interlocked.Exchange(ref unloaded, 1);
			if (previewRoot is not null) previewRoot.Changed -= Root_Changed;
			if (player is { } current)
			{
				current.FrameRendered -= Player_FrameRendered;
				current.StateChanged -= Player_StateChanged;
				current.Dispose();
			}
			lock (frameGate) latestFrame = null;
			Video.Source = null;
			bitmap = null;
			ViewModel.PreviewControlBase_Unloaded(sender, e);
		}
	}
}
#endif
