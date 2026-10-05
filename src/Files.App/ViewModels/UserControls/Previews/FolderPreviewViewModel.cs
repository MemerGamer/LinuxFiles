// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
using Files.Core.Storage.Contracts;
using OwlCore.Storage.System.IO;

namespace Files.App.ViewModels.Previews
{
	public sealed class FolderPreviewViewModel
	{
		private readonly InfoPaneViewModel infoPaneViewModel = Ioc.Default.GetRequiredService<InfoPaneViewModel>();
		public ListedItem Item { get; }

		public BitmapImage? Thumbnail { get; set; } = new();

		public FolderPreviewViewModel(ListedItem item)
			=> Item = item;

		public Task LoadAsync()
			=> LoadPreviewAndDetailsAsync();

		private async Task LoadPreviewAndDetailsAsync()
		{
			var itemPath = Item.ItemPath!;
#if WINDOWS
			var rootItem = await FilesystemTasks.WrapNullable(() => DriveHelpers.GetRootFromPathAsync(itemPath));
			var folder = await StorageFileExtensions.DangerousGetFolderFromPathAsync(itemPath, rootItem.Result)
				?? throw new InvalidOperationException("The preview folder could not be opened.");
#else
			var resolved = await Ioc.Default.GetRequiredService<IStorableResolver>().TryGetAsync(itemPath);
			if (resolved.Item is not IFolder)
				throw new IOException("The preview folder could not be resolved.");
#endif

			var result = await FileThumbnailHelper.GetIconAsync(
				Item.ItemPath,
				Constants.ShellIconSizes.Jumbo,
				true,
				IconOptions.None);

			if (result is not null)
				Thumbnail = await result.ToBitmapAsync();

			// If the selected item is the root of a drive (e.g. "C:\") or a cloud drive,
			// we do not need to load the properties below, since they will not be shown.
			// Drive properties will be obtained through the DrivesViewModel service.
			if (Item.IsDriveRoot || infoPaneViewModel?.SelectedDriveItem is not null)
				return;

#if WINDOWS
			var info = await folder.GetBasicPropertiesAsync();
			var dateModified = info.DateModified;
			var dateCreated = info.DateCreated;
#else
			var localInfo = resolved.Item is SystemFolder ? new DirectoryInfo(itemPath) : null;
			var dateModified = localInfo is null ? Item.ItemDateModifiedReal : new DateTimeOffset(localInfo.LastWriteTime);
			var dateCreated = localInfo is null ? Item.ItemDateCreatedReal : new DateTimeOffset(localInfo.CreationTime);
#endif

			Item.FileDetails =
			[
				GetFileProperty("PropertyItemCount", infoPaneViewModel?.DirectoryItemCount),
				GetFileProperty("PropertyDateModified", dateModified),
				GetFileProperty("PropertyDateCreated", dateCreated),
				GetFileProperty("PropertyParsingPath", itemPath),
			];

			if (GitHelpers.IsRepositoryEx(Item.ItemPath, out var repoPath) &&
				!string.IsNullOrEmpty(repoPath))
			{
				var gitDirectory = GitHelpers.GetGitRepositoryPath(itemPath, Path.GetPathRoot(itemPath));
				var headName = (await GitHelpers.GetRepositoryHead(gitDirectory))?.Name ?? string.Empty;
				var repositoryName = GitHelpers.GetOriginRepositoryName(gitDirectory);

				if (!string.IsNullOrEmpty(gitDirectory))
					Item.FileDetails.Add(GetFileProperty("GitOriginRepositoryName", repositoryName));

				if (!string.IsNullOrWhiteSpace(headName))
					Item.FileDetails.Add(GetFileProperty("GitCurrentBranch", headName));
			}
		}

		private static FileProperty GetFileProperty(string nameResource, object? value)
			=> new() { NameResource = nameResource, Value = value };
	}
}
