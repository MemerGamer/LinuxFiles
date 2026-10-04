// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.EventArguments;
using Files.App.Utils.CommandLine;
using Files.App.ViewModels;
using Files.App.Views;
using Files.Platform.Abstractions.Instance;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Animation;
using System.IO;

namespace Files.App
{
	/// <summary>
	/// Turns command lines (first launch, forwarded second launches) and FileManager1 requests into navigation on the desktop (Linux) build.
	/// Replaces the WinAppSDK activation switch in <c>MainWindow.InitializeApplicationAsync</c>.
	/// </summary>
	internal static class DesktopActivation
	{
		/// <summary>
		/// Converts a request into command line options.
		/// </summary>
		public static DesktopLaunchOptions ToOptions(InstanceRequest request)
		{
			switch (request.Kind)
			{
				case InstanceRequestKind.ShowFolders:
					return new DesktopLaunchOptions(request.Arguments, [], false, false);

				case InstanceRequestKind.ShowItems:
				case InstanceRequestKind.ShowItemProperties:
					// LINUX-TODO(properties): ShowItemProperties should also open the properties dialog for the item; for now it only reveals it
					return new DesktopLaunchOptions([], request.Arguments, false, false);

				default:
					return DesktopCommandLine.Parse(request.Arguments, request.WorkingDirectory);
			}
		}

		/// <summary>
		/// Opens what the options ask for. Must run on the UI thread. With nothing to open, only shows the window (or the default start page on the first call).
		/// </summary>
		public static async Task ApplyAsync(DesktopLaunchOptions options, bool isFirstLaunch)
		{
			var window = MainWindow.Instance;
			var rootFrame = window.EnsureRootFrame();
			if (rootFrame is null)
				return;

			var targets = BuildTargets(options);

			var hasMainPage = rootFrame.Content is MainPage && MainPageViewModel.AppInstances.Any();
			if (!hasMainPage)
			{
				// First start (or the main page is gone): the first target becomes the initial tab
				if (targets.Count == 0)
				{
					rootFrame.Navigate(typeof(MainPage), null, new SuppressNavigationTransitionInfo());
				}
				else
				{
					rootFrame.Navigate(typeof(MainPage), targets[0], new SuppressNavigationTransitionInfo());
					targets.RemoveAt(0);
				}
			}

			// LINUX-TODO(window): --new-window is treated as a new tab because the app has a single main window
			foreach (var target in targets)
				await OpenTargetAsync(target);

			if (!isFirstLaunch || hasMainPage)
				window.Activate();
		}

		private static List<PaneNavigationArguments> BuildTargets(DesktopLaunchOptions options)
		{
			var targets = new List<PaneNavigationArguments>();

			foreach (var path in options.Paths)
			{
				// A file path reveals the file in its folder instead of failing to open it as a folder
				if (File.Exists(path) && Path.GetDirectoryName(path) is { Length: > 0 } parent)
					targets.Add(new() { LeftPaneNavPathParam = parent, LeftPaneSelectItemParam = Path.GetFileName(path) });
				else
					targets.Add(new() { LeftPaneNavPathParam = path });
			}

			foreach (var path in options.Selects)
			{
				if (Path.GetDirectoryName(path) is { Length: > 0 } parent)
					targets.Add(new() { LeftPaneNavPathParam = parent, LeftPaneSelectItemParam = Path.GetFileName(path) });
			}

			return targets;
		}

		private static async Task OpenTargetAsync(PaneNavigationArguments target)
		{
			try
			{
				var existing = MainPageViewModel.AppInstances
					.Select((tab, index) => (tab, index))
					.FirstOrDefault(x => x.tab.NavigationParameter?.NavigationParameter is PaneNavigationArguments existingArgs
						&& existingArgs.LeftPaneNavPathParam == target.LeftPaneNavPathParam
						&& target.LeftPaneSelectItemParam is null);

				if (existing.tab is not null)
					App.AppModel.TabStripSelectedIndex = existing.index;
				else
					await NavigationHelpers.AddNewTabByParamAsync(typeof(ShellPanesPage), target);
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Failed to open a requested location.");
			}
		}
	}
}
