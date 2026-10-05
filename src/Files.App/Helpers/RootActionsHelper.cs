// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Clipboard;
using Files.Platform.Abstractions.Elevation;
using Files.Platform.Linux.Elevation;
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

		private static bool dialogOpen;

		public static async Task DeleteAsync(IReadOnlyList<string> paths)
		{
			if (Elevation is { } elevation)
				await ConfirmAndRunAsync(elevation, new ElevationPlanPreview(_ => elevation.PlanDelete(paths)), null);
		}

		public static async Task RenameAsync(string path)
		{
			if (Elevation is { } elevation)
				await ConfirmAndRunAsync(elevation, new ElevationPlanPreview(name => elevation.PlanRename(path, name)), Path.GetFileName(path));
		}

		public static async Task PasteAsync(string destinationFolder)
		{
			if (Elevation is not { } elevation || Ioc.Default.GetService<IClipboardService>() is not { } clipboard)
				return;

			var files = await clipboard.GetFilesAsync();
			if (files is null || files.Paths.Count == 0)
				return;

			var move = files.Operation == ClipboardOperation.Cut;
			await ConfirmAndRunAsync(elevation, new ElevationPlanPreview(_ => move
				? elevation.PlanMove(files.Paths, destinationFolder)
				: elevation.PlanCopy(files.Paths, destinationFolder)), null);
		}

		// The plan that runs is the one object the preview hands out when the confirm button is clicked, and only when its text is on screen.
		// With a name box (rename) the plan is recomputed on every keystroke and confirm stays disabled while it is invalid.
		private static async Task ConfirmAndRunAsync(IElevationService elevation, ElevationPlanPreview preview, string? initialName)
		{
			if (dialogOpen)
				return;

			dialogOpen = true;
			try
			{
				preview.Update(initialName ?? string.Empty);
				var text = new TextBlock { Text = preview.DisplayText, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
				TextBox? box = initialName is null ? null : new TextBox { Text = initialName };
				var dialog = CreateDialog(text, box);
				dialog.IsPrimaryButtonEnabled = preview.CanConfirm;

				if (box is not null)
				{
					box.TextChanged += (_, _) =>
					{
						preview.Update(box.Text);
						text.Text = preview.DisplayText;
						dialog.IsPrimaryButtonEnabled = preview.CanConfirm;
					};
				}

				ElevatedPlan? confirmed = null;
				dialog.PrimaryButtonClick += (_, args) =>
				{
					confirmed = preview.Confirm(text.Text);
					args.Cancel = confirmed is null;
				};

				var result = await dialog.TryShowAsync();
				preview.Close();
				if (result == ContentDialogResult.Primary && confirmed is not null)
					await RunAsync(elevation, confirmed);
			}
			finally
			{
				dialogOpen = false;
			}
		}

		private static async Task RunAsync(IElevationService elevation, ElevatedPlan plan)
		{
			await ReportAsync(await elevation.RunAsync(plan));

			if (Ioc.Default.GetRequiredService<IContentPageContext>().ShellPage is { } shellPage)
				await shellPage.RefreshIfNoWatcherExistsAsync();
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
