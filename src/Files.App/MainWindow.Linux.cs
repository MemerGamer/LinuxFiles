// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Files.Platform.Abstractions;
using Files.Platform.Abstractions.Appearance;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Files.Platform.Linux.Windowing;
using Files.Platform.Linux.Elevation;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Uno.UI.NativeElementHosting;

namespace Files.App
{
	public sealed partial class MainWindow
	{
		private X11WindowChrome? _linuxChrome;
		private PropertyChangedEventHandler? _appearanceChanged;
		private EventHandler? _systemAppearanceChanged;
		private bool _chromeEventsAttached;
		private int _dragStart;
		private int _titleHeight;
		private string? _linuxWindowTitle;
		public bool UseClientSideDecorations => OperatingSystem.IsLinux() &&
			Ioc.Default.GetRequiredService<ILocalSettingsStore>().Get("UseClientSideDecorations", true);
		public bool HasClientSideDecorations => _linuxChrome?.SupportsClientSideDecorations == true && UseClientSideDecorations;
		public bool IsLinuxWindowMaximized => _linuxChrome?.IsMaximized ?? false;
		public event EventHandler? LinuxChromeChanged;

		internal static string FormatRootModeTitle(string title) => title + (RootActionsAvailability.Mode.Indicator switch
		{
			RootModeIndicator.RootMode => Strings.LinuxRootModeSuffix.GetLocalizedResource(),
			RootModeIndicator.RunningAsRoot => Strings.LinuxRunningAsRootSuffix.GetLocalizedResource(),
			_ => string.Empty,
		});

		public void InitializeLinuxChrome()
		{
			if (!OperatingSystem.IsLinux())
				return;

			if (_linuxChrome is null && Uno.UI.Xaml.WindowHelper.GetNativeWindow(this) is X11NativeWindow native)
				_linuxChrome = X11WindowChrome.TryCreate((nuint)native.WindowId);

			if (!HasClientSideDecorations)
				_linuxChrome?.HideRegions();
			if (_linuxWindowTitle is not null)
				_linuxChrome?.SetTitle(_linuxWindowTitle);

			ExtendsContentIntoTitleBar = HasClientSideDecorations;
			if (AppWindow.Presenter is OverlappedPresenter presenter)
				presenter.SetBorderAndTitleBar(!HasClientSideDecorations, !HasClientSideDecorations);

			if (!_chromeEventsAttached)
			{
				_chromeEventsAttached = true;
				AppWindow.Changed += (_, _) =>
				{
					UpdateLinuxChromeRegions(_dragStart, _titleHeight);
					LinuxChromeChanged?.Invoke(this, EventArgs.Empty);
				};
				Closed += (_, _) =>
				{
					Ioc.Default.GetRequiredService<IAppearanceSettingsService>().PropertyChanged -= _appearanceChanged;
					Ioc.Default.GetRequiredService<ISystemAppearanceService>().Changed -= _systemAppearanceChanged;
					_linuxChrome?.Dispose(); _linuxChrome = null;
				};

				var appearance = Ioc.Default.GetRequiredService<IAppearanceSettingsService>();
				_appearanceChanged = (_, e) =>
				{
					if (e.PropertyName is nameof(IAppearanceSettingsService.WindowOpacity) or nameof(IAppearanceSettingsService.BackdropMode) or nameof(IAppearanceSettingsService.BackgroundOpacity))
						DispatcherQueue.TryEnqueue(ApplyLinuxWindowOpacity);
				};
				appearance.PropertyChanged += _appearanceChanged;
				_systemAppearanceChanged = (_, _) => DispatcherQueue.TryEnqueue(ApplyLinuxWindowOpacity);
				Ioc.Default.GetRequiredService<ISystemAppearanceService>().Changed += _systemAppearanceChanged;
			}
			ApplyLinuxWindowOpacity();
			Ioc.Default.GetRequiredService<IResourcesService>().ApplyResources();
			LinuxChromeChanged?.Invoke(this, EventArgs.Empty);
		}

		private void ApplyLinuxWindowOpacity()
		{
			var appearance = Ioc.Default.GetRequiredService<IAppearanceSettingsService>();
			var highContrast = Ioc.Default.GetRequiredService<ISystemAppearanceService>().Current.HighContrast == true;
			var support = X11AppearanceSupport.Current;
			_linuxChrome?.SetWindowOpacity(!highContrast && support.WindowOpacity == OpacitySupport.Supported ? appearance.WindowOpacity : 1);
			var mode = support.Resolve(appearance.BackdropMode, highContrast);
			Uno.UI.Xaml.WindowHelper.SetBackground(this, new SolidColorBrush(mode == BackdropMode.Solid ?
				(appearance.AppThemeMode == "Dark" || appearance.AppThemeMode == "Default" && Files.Platform.Linux.Theme.LinuxColorScheme.IsDark ? Colors.Black : Colors.White) : Colors.Transparent));
			_linuxChrome?.SetBlur(mode == BackdropMode.Blur);
		}

		public void SetClientSideDecorations(bool enabled)
		{
			Ioc.Default.GetRequiredService<ILocalSettingsStore>().Set("UseClientSideDecorations", enabled);
			InitializeLinuxChrome();
		}

		public void UpdateLinuxWindowTitle(string title)
		{
			// Uno 6.7 can leave the native title unchanged after setting AppWindow.Title.
			_linuxWindowTitle = title;
			_linuxChrome?.SetTitle(title);
		}

		public void ToggleLinuxWindowMaximize() => _linuxChrome?.ToggleMaximize();

		public void UpdateLinuxChromeRegions(int dragStart, int titleHeight)
		{
			_dragStart = dragStart;
			_titleHeight = titleHeight;
			if (!HasClientSideDecorations || Content is not FrameworkElement root || root.XamlRoot is null)
				return;

			var scale = root.XamlRoot.RasterizationScale;
			_linuxChrome?.SetRegions((int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale),
				(int)(dragStart * scale), (int)((root.ActualWidth - dragStart - 138) * scale),
				(int)(titleHeight * scale), Math.Max(1, (int)(5 * scale)));
		}
	}
}
#endif
