// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Files.Platform.Abstractions;
using Files.Platform.Abstractions.Launching;
using Microsoft.Extensions.Logging;

namespace Files.App.Data.Factories
{
	public static partial class ContentPageContextFlyoutFactory
	{
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
