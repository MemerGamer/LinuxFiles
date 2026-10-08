// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Windowing;

namespace Files.App.UserControls.TabBar
{
	public sealed partial class TabBar
	{
		private void InitializeLinuxCaptionButtons()
		{
#if !WINDOWS
			if (!OperatingSystem.IsLinux())
				return;

			MainWindow.Instance.InitializeLinuxChrome();
			MainWindow.Instance.LinuxChromeChanged -= LinuxChromeChanged;
			MainWindow.Instance.LinuxChromeChanged += LinuxChromeChanged;
			Unloaded -= LinuxCaptionButtons_Unloaded;
			Unloaded += LinuxCaptionButtons_Unloaded;
			UpdateLinuxCaptionButtons();
			_ = NavigationHelpers.UpdateInstancePropertiesAsync(Items.ElementAtOrDefault(App.AppModel.TabStripSelectedIndex)?.NavigationParameter?.NavigationParameter);
#endif
		}

#if !WINDOWS
		private void LinuxCaptionButtons_Unloaded(object sender, RoutedEventArgs e)
			=> MainWindow.Instance.LinuxChromeChanged -= LinuxChromeChanged;

		private void LinuxChromeChanged(object? sender, EventArgs e) => UpdateLinuxCaptionButtons();

		private void UpdateLinuxCaptionButtons()
		{
			var window = MainWindow.Instance;
			LinuxCaptionButtons.Visibility = window.HasClientSideDecorations ? Visibility.Visible : Visibility.Collapsed;
			ClientSideDecorationsMenuItem.Visibility = Visibility.Visible;
			ClientSideDecorationsMenuItem.IsChecked = window.UseClientSideDecorations;
			var maximizeLabel = (window.IsLinuxWindowMaximized ? "LinuxRestoreWindow" : "LinuxMaximizeWindow").GetLocalizedResource();
			AutomationProperties.SetName(LinuxMaximizeButton, maximizeLabel);
			ToolTipService.SetToolTip(LinuxMaximizeButton, maximizeLabel);
			LinuxMaximizeIcon.Glyph = window.IsLinuxWindowMaximized ? "\uE923" : "\uE922";
			UpdateTitleBarInsets();
		}
#endif

		private void ClientSideDecorationsMenuItem_Click(object sender, RoutedEventArgs e)
		{
#if !WINDOWS
			MainWindow.Instance.SetClientSideDecorations(ClientSideDecorationsMenuItem.IsChecked);
#endif
		}

		private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
		{
			if (MainWindow.Instance.AppWindow.Presenter is OverlappedPresenter presenter)
				presenter.Minimize();
		}

		private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
		{
#if !WINDOWS
			MainWindow.Instance.ToggleLinuxWindowMaximize();
#endif
		}

		private void CloseWindow_Click(object sender, RoutedEventArgs e) => MainWindow.Instance.Close();
	}
}
