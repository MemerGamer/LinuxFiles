// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Runtime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT;

namespace Files.App
{
	/// <summary>
	/// Represents the entry point of UI for Files app.
	/// </summary>
	public partial class App : Application
	{

		public static TaskCompletionSource? SplashScreenLoadingTCS { get; private set; }
		public static string? OutputPath { get; set; }

		private static FlyoutBase? _LastOpenedFlyout;
		private static bool _isWindowTeardownCompleted;
		public static FlyoutBase? LastOpenedFlyout
		{
			set
			{
				_LastOpenedFlyout = value;

				if (_LastOpenedFlyout is not null)
					_LastOpenedFlyout.Closed += LastOpenedFlyout_Closed;
			}
		}

		// TODO: Replace with DI
		public static QuickAccessManager QuickAccessManager { get; private set; } = null!;
		public static StorageHistoryWrapper HistoryWrapper { get; private set; } = null!;
		public static FileTagsManager FileTagsManager { get; private set; } = null!;
		public static LibraryManager LibraryManager { get; private set; } = null!;
		public static AppModel AppModel { get; private set; } = null!;
		public static ILogger Logger { get; private set; } = NullLogger.Instance;

		public static Microsoft.UI.Dispatching.DispatcherQueue? UiDispatcher { get; private set; }

		/// <summary>
		/// Initializes an instance of <see cref="App"/>.
		/// </summary>
		public App()
		{
			InitializeComponent();

			// Configure exception handlers
			AppLifecycleHelper.RecordFirstChanceExceptions();
			UnhandledException += (sender, e) =>
			{
				if (DesktopRuntimeGuards.TryHandleRecoverable(e))
					return;
				AppLifecycleHelper.HandleAppUnhandledException(e.Exception, true, "Application.UnhandledException", e.Message);
			};
			AppDomain.CurrentDomain.UnhandledException += (sender, e) => AppLifecycleHelper.HandleAppUnhandledException(e.ExceptionObject as Exception, false, "AppDomain.UnhandledException");
			TaskScheduler.UnobservedTaskException += (sender, e) =>
			{
				e.SetObserved();
				AppLifecycleHelper.HandleAppUnhandledException(e.Exception, false, "TaskScheduler.UnobservedTaskException");
			};
			AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
				SafetyExtensions.IgnoreExceptions(() => Ioc.Default.GetService<FileLoggerProvider>()?.TryCompleteAndFlush(TimeSpan.FromSeconds(2)));
			AppDomain.CurrentDomain.ProcessExit += static (_, _) => FileTagsHelper.DrainPendingWritesAsync().GetAwaiter().GetResult();
			AppDomain.CurrentDomain.ProcessExit += static (_, _) => SafetyExtensions.IgnoreExceptions(ArchiveOpenTempStore.CleanupCurrentProcess);
			_ = Task.Run(ArchiveOpenTempStore.CleanupStale);
		}


		/// <summary>
		/// Gets invoked when the last opened flyout is closed.
		/// </summary>
		[DynamicWindowsRuntimeCast(typeof(FlyoutBase))]
		private static void LastOpenedFlyout_Closed(object? sender, object e)
		{
			if (sender is not FlyoutBase flyoutBase)
				return;

			flyoutBase.Closed -= LastOpenedFlyout_Closed;
			if (_LastOpenedFlyout == flyoutBase)
				_LastOpenedFlyout = null;
		}
	}
}
