// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Clipboard;
using Files.Platform.Abstractions.Elevation;
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
			if (Elevation is not { } elevation || paths.Count == 0)
				return;

			var commands = paths.Select(elevation.PlanDelete).ToList();
			if (commands.Any(c => c is null) || !await ConfirmAsync(commands!, null))
				return;

			await RunAsync(paths.Select<string, Func<Task<ElevatedResult>>>(path => () => elevation.DeleteAsync(path)).ToList());
		}

		public static async Task RenameAsync(string path)
		{
			if (Elevation is not { } elevation)
				return;

			var box = new TextBox { Text = Path.GetFileName(path) };
			var preview = new TextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
			void Update() => preview.Text = elevation.PlanRename(path, box.Text)?.DisplayText ?? string.Empty;
			box.TextChanged += (_, _) => Update();
			Update();

			var dialog = CreateDialog(preview, box);
			dialog.IsPrimaryButtonEnabled = true;
			if (await dialog.TryShowAsync() != ContentDialogResult.Primary || elevation.PlanRename(path, box.Text) is null)
				return;

			await ReportAsync(await elevation.RenameAsync(path, box.Text));
		}

		public static async Task PasteAsync(string destinationFolder)
		{
			if (Elevation is not { } elevation || Ioc.Default.GetService<IClipboardService>() is not { } clipboard)
				return;

			var files = await clipboard.GetFilesAsync();
			if (files is null || files.Paths.Count == 0)
				return;

			var move = files.Operation == ClipboardOperation.Cut;
			var commands = files.Paths.Select(p => move ? elevation.PlanMove(p, destinationFolder) : elevation.PlanCopy(p, destinationFolder)).ToList();
			if (commands.Any(c => c is null) || !await ConfirmAsync(commands!, null))
				return;

			await RunAsync(files.Paths.Select<string, Func<Task<ElevatedResult>>>(path => () => move
				? elevation.MoveAsync(path, destinationFolder)
				: elevation.CopyAsync(path, destinationFolder)).ToList());
		}

		private static async Task RunAsync(IReadOnlyList<Func<Task<ElevatedResult>>> operations)
		{
			foreach (var operation in operations)
			{
				if (!await ReportAsync(await operation()))
					break;
			}

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
				Content = string.Format(Strings.RootActionFailed.GetLocalizedResource(), result.Error),
				PrimaryButtonText = Strings.OK.GetLocalizedResource(),
				XamlRoot = MainWindow.Instance.Content.XamlRoot,
			};

			await dialog.TryShowAsync();
			return false;
		}

		private static async Task<bool> ConfirmAsync(IReadOnlyList<ElevatedCommand> commands, UIElement? extra)
		{
			var text = new TextBlock
			{
				Text = string.Join(Environment.NewLine, commands.Select(c => c.DisplayText)),
				IsTextSelectionEnabled = true,
				TextWrapping = TextWrapping.Wrap,
			};

			return await CreateDialog(text, extra).TryShowAsync() == ContentDialogResult.Primary;
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
