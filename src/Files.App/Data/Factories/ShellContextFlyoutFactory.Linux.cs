// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Files.App.Dialogs;
using Files.Platform.Abstractions.Launching;
using Files.Platform.Abstractions.Mime;
using System.IO;

namespace Files.App.Helpers
{
	public static partial class ShellContextFlyoutFactory
	{
		/// <summary>
		/// Linux has no shell extension menu. The only entry produced is the "Open with" submenu for a single file,
		/// shaped like the Windows "openas" entry so the existing layout code swaps it in.
		/// </summary>
		private static Task<List<ContextMenuFlyoutItemViewModel>> GetLinuxContextMenuAsync(List<ListedItem>? selectedItems, CancellationToken cancellationToken)
		{
			var result = new List<ContextMenuFlyoutItemViewModel>();

			if (selectedItems is { Count: 1 } &&
				selectedItems[0].ItemPath is { Length: > 0 } path &&
				selectedItems[0].PrimaryItemAttribute == Windows.Storage.StorageItemTypes.File)
			{
				var openWith = new ContextMenuFlyoutItemViewModel
				{
					Text = Strings.OpenWith.GetLocalizedResource(),
					Tag = new Win32ContextMenuItem { CommandString = "openas" },
					Items = [],
				};
				openWith.LoadSubMenuAction = () => LoadOpenWithItemsAsync(openWith, path, cancellationToken);
				result.Add(openWith);
			}

			return Task.FromResult(result);
		}

		private static async Task LoadOpenWithItemsAsync(ContextMenuFlyoutItemViewModel model, string path, CancellationToken cancellationToken)
		{
			var mimeTypes = Ioc.Default.GetRequiredService<IMimeTypeService>();
			var registry = Ioc.Default.GetRequiredService<IApplicationRegistry>();
			var launcher = Ioc.Default.GetRequiredService<ILauncherService>();

			var mime = await mimeTypes.GetMimeTypeAsync(path, cancellationToken);
			var apps = await registry.GetApplicationsForMimeTypeAsync(mime, cancellationToken);
			var defaultApp = await registry.GetDefaultApplicationAsync(mime, cancellationToken);
			var items = model.Items ??= [];

			foreach (var app in apps)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var captured = app;
				items.Add(new ContextMenuFlyoutItemViewModel
				{
					Text = captured.Id == defaultApp?.Id ? $"{captured.Name} ({Strings.Default.GetLocalizedResource()})" : captured.Name,
					BitmapIcon = await LinuxAppIcons.GetBitmapAsync(captured),
					Command = new AsyncRelayCommand(() => launcher.OpenWithAsync(captured, [path])),
				});
			}

			if (items.Count > 0)
				items.Add(new ContextMenuFlyoutItemViewModel { ItemType = ContextMenuFlyoutItemType.Separator });

			items.Add(new ContextMenuFlyoutItemViewModel
			{
				Text = Strings.ChooseAnotherApp.GetLocalizedResource() + "…",
				Command = new AsyncRelayCommand(() => LinuxOpenWithDialog.ShowAsync(path)),
			});
		}
	}
}
#endif
