// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Clipboard;
using Files.Platform.Abstractions.Elevation;
using Files.Platform.Linux.Launching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.IO;

namespace Files.App.Helpers
{
	/// <summary>
	/// Linux "Root actions" (like Dolphin's): delete, rename and paste as root through <see cref="IElevationService"/>.
	/// Every operation shows the exact command first and the system's polkit prompt authenticates it.
	/// </summary>
	internal static class RootActionsHelper
	{
		private static IElevationService? Elevation => OperatingSystem.IsLinux() ? Ioc.Default.GetService<IElevationService>() : null;

		public static bool IsAvailable => Elevation?.IsAvailable ?? false;

		public static async Task DeleteAsync(IReadOnlyList<string> paths)
		{
			if (Elevation is { } elevation)
				await ConfirmAndRunAsync(elevation, elevation.PlanDelete(paths));
		}

		public static async Task RenameAsync(string path)
		{
			if (Elevation is not { } elevation)
				return;

			var box = new TextBox { Text = Path.GetFileName(path) };
			var preview = new TextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
			void Update() => preview.Text = Describe(elevation.PlanRename(path, box.Text));
			box.TextChanged += (_, _) => Update();
			Update();

			var dialog = CreateDialog(preview, box);
			if (await dialog.TryShowAsync() != ContentDialogResult.Primary)
				return;

			// The plan that is run is the one for the name that was confirmed
			var plan = elevation.PlanRename(path, box.Text);
			if (plan.Plan is not null)
				await RunAsync(elevation, plan.Plan);
		}

		public static async Task PasteAsync(string destinationFolder)
		{
			if (Elevation is not { } elevation || Ioc.Default.GetService<IClipboardService>() is not { } clipboard)
				return;

			var files = await clipboard.GetFilesAsync();
			if (files is null || files.Paths.Count == 0)
				return;

			await ConfirmAndRunAsync(elevation, files.Operation == ClipboardOperation.Cut
				? elevation.PlanMove(files.Paths, destinationFolder)
				: elevation.PlanCopy(files.Paths, destinationFolder));
		}

		// Shows the plan, and runs that very object when confirmed
		private static async Task ConfirmAndRunAsync(IElevationService elevation, ElevatedPlanResult planned)
		{
			var text = new TextBlock { Text = Describe(planned), IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
			var dialog = CreateDialog(text, null);
			dialog.IsPrimaryButtonEnabled = planned.Plan is not null;
			if (await dialog.TryShowAsync() == ContentDialogResult.Primary && planned.Plan is not null)
				await RunAsync(elevation, planned.Plan);
		}

		private static async Task RunAsync(IElevationService elevation, ElevatedPlan plan)
		{
			await ReportAsync(await elevation.RunAsync(plan));

			if (Ioc.Default.GetRequiredService<IContentPageContext>().ShellPage is { } shellPage)
				await shellPage.RefreshIfNoWatcherExistsAsync();
		}

		// One escaped argument per line, never truncated; plans that cannot be shown completely are not offered
		private static string Describe(ElevatedPlanResult planned)
		{
			if (planned.Plan is not { } plan)
				return DisplaySanitizer.Field(planned.Refusal, 400);

			var blocks = new List<string>();
			foreach (var command in plan.Commands)
			{
				var lines = DisplaySanitizer.FullArguments(["pkexec", command.Program, .. command.Arguments]);
				if (lines is null)
					return Strings.RootActionFailed.GetLocalizedResource().Replace("{0}", "too many items to display");

				blocks.Add(string.Join(Environment.NewLine, lines));
			}

			return string.Join(Environment.NewLine + Environment.NewLine, blocks);
		}

		private static async Task<bool> ReportAsync(ElevatedResult result)
		{
			if (result.Succeeded || result.WasDismissed)
				return result.Succeeded;

			var dialog = new ContentDialog
			{
				Title = Strings.RootActions.GetLocalizedResource(),
				Content = string.Format(Strings.RootActionFailed.GetLocalizedResource(), DisplaySanitizer.Field(result.Error, 400)),
				PrimaryButtonText = Strings.OK.GetLocalizedResource(),
				XamlRoot = MainWindow.Instance.Content.XamlRoot,
			};

			await dialog.TryShowAsync();
			return false;
		}

		private static ContentDialog CreateDialog(UIElement commandText, UIElement? extra)
		{
			var panel = new StackPanel { Spacing = 8 };
			if (extra is not null)
				panel.Children.Add(extra);
			panel.Children.Add(new TextBlock { Text = Strings.RootConfirmContent.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap });
			panel.Children.Add(commandText);

			return new ContentDialog
			{
				Title = Strings.RootConfirmTitle.GetLocalizedResource(),
				Content = panel,
				PrimaryButtonText = Strings.RootRunButton.GetLocalizedResource(),
				CloseButtonText = Strings.Cancel.GetLocalizedResource(),
				DefaultButton = ContentDialogButton.Close,
				XamlRoot = MainWindow.Instance.Content.XamlRoot,
			};
		}
	}
}
