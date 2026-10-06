// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Files.Platform.Abstractions;
using Files.Platform.Abstractions.Launching;
using Files.Platform.Abstractions.Mime;
using Files.Platform.Linux.Launching;
using Files.Platform.Linux.Mime;
using Microsoft.Extensions.Logging;

namespace Files.App.Data.Factories
{
	public static partial class ContentPageContextFlyoutFactory
	{
		private static async Task<List<ContextMenuFlyoutItemViewModel>> GetLinuxItemContextCommandsAsync(
			string? workingDir, List<ListedItem> selectedItems, bool shiftPressed, bool showOpenMenu, CancellationToken cancellationToken)
		{
			var items = await ShellContextFlyoutFactory.GetShellContextmenuAsync(shiftPressed: shiftPressed, showOpenMenu: showOpenMenu,
				workingDirectory: workingDir, selectedItems: selectedItems, cancellationToken: cancellationToken);
			if (selectedItems.Count == 0 || selectedItems.Any(i => i.IsArchive || i.IsRecycleBinItem || i.IsFtpItem))
				return items;
			var targets = selectedItems.Select(i => i.ItemPath ?? string.Empty).ToArray();
			if (!await Task.Run(() => targets.All(LinuxServiceMenuService.IsLocalFileSystemTarget), cancellationToken))
				return items;

			try
			{
				var actions = await Ioc.Default.GetRequiredService<IServiceMenuService>().GetActionsAsync(targets, cancellationToken);
				var groups = new Dictionary<string, ContextMenuFlyoutItemViewModel>(StringComparer.Ordinal);
				foreach (var action in actions)
				{
					var captured = action;
					var item = new ContextMenuFlyoutItemViewModel
					{
						Text = DisplaySanitizer.Field(action.Application.Name),
						Glyph = "\xE756",
						ShowInSearchPage = true,
						Command = new AsyncRelayCommand(() => NavigationHelpers.RunServiceMenuLinuxAsync(captured, targets)),
					};
					if (string.IsNullOrWhiteSpace(action.Submenu))
						items.Add(item);
					else
					{
						if (!groups.TryGetValue(action.Submenu, out var group))
						{
							group = new ContextMenuFlyoutItemViewModel { Text = DisplaySanitizer.Field(action.Submenu), Items = [], ShowInSearchPage = true };
							groups.Add(action.Submenu, group);
							items.Add(group);
						}
						group.Items!.Add(item);
					}
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Failed to list service menus");
			}
			return items;
		}

		/// <summary>
		/// Builds the "New" submenu from <see cref="ITemplatesService"/>: Folder, Text Document and the files in ~/Templates.
		/// </summary>
		internal static List<ContextMenuFlyoutItemViewModel> GetLinuxNewItemItems(bool canCreateFileInPage)
		{
			var list = new List<ContextMenuFlyoutItemViewModel>()
			{
				new ContextMenuFlyoutItemViewModelBuilder(Commands.CreateFolder).Build(),
			};

			if (!canCreateFileInPage)
				return list;

			try
			{
				var templates = Ioc.Default.GetRequiredService<ITemplatesService>();
				var directories = Ioc.Default.GetRequiredService<IUserDirectories>();
				var entries = templates.GetTemplatesAsync(directories.Templates).GetAwaiter().GetResult();

				var addedSeparator = false;
				foreach (var template in entries)
				{
					if (template.Kind == NewItemKind.Folder)
						continue;

					if (template.Kind == NewItemKind.TemplateFile && !addedSeparator)
					{
						list.Add(new ContextMenuFlyoutItemViewModel { ItemType = ContextMenuFlyoutItemType.Separator });
						addedSeparator = true;
					}

					var captured = template;
					list.Add(new ContextMenuFlyoutItemViewModel
					{
						Text = template.Kind == NewItemKind.EmptyFile ? Strings.TextDocument.GetLocalizedResource() : template.Name,
						Glyph = "",
						Command = new AsyncRelayCommand(() => CreateFromTemplateAsync(captured)),
						ShowInFtpPage = true,
						ShowInZipPage = true,
					});
				}
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Failed to list the New menu templates");
			}

			return list;
		}

		private static async Task CreateFromTemplateAsync(NewItemTemplate template)
		{
			try
			{
				var shellPage = Ioc.Default.GetRequiredService<IContentPageContext>().ShellPage;
				var folder = shellPage?.ShellViewModel?.WorkingDirectory;
				if (shellPage is null || string.IsNullOrEmpty(folder))
					return;

				var name = template.Kind == NewItemKind.EmptyFile
					? Strings.NewTextDocument.GetLocalizedResource() + System.IO.Path.GetExtension(template.DefaultFileName)
					: null;

				var createdPath = await Ioc.Default.GetRequiredService<ITemplatesService>().CreateFromTemplateAsync(template, folder, name);
				await shellPage.RefreshIfNoWatcherExistsAsync();
				await UIFilesystemHelpers.SelectAndRenameNewItemAsync(shellPage, createdPath);
			}
			catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
			{
				App.Logger.LogWarning(ex, "Failed to create an item from a template");
				await DialogDisplayHelper.ShowDialogAsync(Strings.AccessDenied.GetLocalizedResource(), Strings.AccessDeniedCreateDialogText.GetLocalizedResource());
			}
		}
	}
}
#endif
