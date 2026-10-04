// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Files.App
{
	/// <summary>
	/// Desktop (Uno Skia) startup path. Replaces the WinAppSDK AppInstance/activation, system tray and
	/// "keep running in background" logic in <c>App.xaml.cs</c>, which is compiled for Windows only.
	/// </summary>
	public partial class App
	{
		/// <summary>
		/// Gets invoked when the application is launched. Single instance handling and activation redirection are TODO
		/// (planned behind <c>ISingleInstanceService</c>).
		/// </summary>
		protected override void OnLaunched(LaunchActivatedEventArgs e)
		{
			UiDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

			// Constructed on the UI thread: the ctor subscribes the UI-thread-only Clipboard.ContentChanged
			AppModel = new AppModel();

			_ = ActivateAsync();

			async Task ActivateAsync()
			{
				var serviceProvider = AppLifecycleHelper.ConfigureHost(AppModel);
				Ioc.Default.ConfigureServices(serviceProvider);

				if (AppLifecycleHelper.AppEnvironment is not AppEnvironment.Dev)
					AppLifecycleHelper.ConfigureSentry();

				// TODO: Replace with DI
				QuickAccessManager = Ioc.Default.GetRequiredService<QuickAccessManager>();
				HistoryWrapper = Ioc.Default.GetRequiredService<StorageHistoryWrapper>();
				FileTagsManager = Ioc.Default.GetRequiredService<FileTagsManager>();
				LibraryManager = Ioc.Default.GetRequiredService<LibraryManager>();
				Logger = Ioc.Default.GetRequiredService<ILogger<App>>();
				AppModel = Ioc.Default.GetRequiredService<AppModel>();

				MainWindow.Instance.Activate();

				MainWindow.Instance.Closed += Window_Closed;
				MainWindow.Instance.Activated += Window_Activated;

				Logger.LogInformation("App launched (desktop).");

				// TODO: pass command line arguments (paths, --select, etc.) once the activation path is ported
				await MainWindow.Instance.InitializeApplicationAsync(null);

				await AppLifecycleHelper.InitializeAppComponentsAsync();
			}
		}

		private void Window_Activated(object sender, WindowActivatedEventArgs args)
		{
			var isActive = args.WindowActivationState != Windows.UI.Core.CoreWindowActivationState.Deactivated;

			ActiveSessionTracker.OnActivationChanged(isActive);

			if (isActive)
				AppModel.IsMainWindowClosed = false;
		}

		private async void Window_Closed(object sender, WindowEventArgs args)
		{
			// Stop dispatcher timers before the close handler yields and window teardown begins
			AppModel.IsMainWindowClosed = true;

			var userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
			var commandManager = Ioc.Default.GetRequiredService<ICommandManager>();

			ActiveSessionTracker.OnActivationChanged(false);

			if (userSettingsService.GeneralSettingsService.ContinueLastSessionOnStartUp || userSettingsService.AppSettingsService.RestoreTabsOnStartup)
				AppLifecycleHelper.SaveSessionTabs();
			else
				await commandManager.CloseAllTabs.ExecuteAsync();

			// TODO: "leave app running" (background parking) is not supported on desktop yet

			FilePropertiesHelpers.DestroyCachedWindows();
			FileOperationsHelpers.WaitForCompletion();
		}
	}
}
