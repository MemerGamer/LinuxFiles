// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using Windows.Storage;
using Windows.Storage.Search;
using WinRT;

namespace Files.App.Utils.Storage
{
	public static partial class StorageFileExtensions
	{
		[DynamicWindowsRuntimeCast(typeof(StorageFile))]
		public static BaseStorageFile? AsBaseStorageFile(this IStorageItem? item)
		{
			if (item is null || !item.IsOfType(StorageItemTypes.File))
				return null;

			return item is StorageFile file ? new SystemStorageFile(file) : item as BaseStorageFile;
		}

		public static async Task<List<IStorageItem>> ToStandardStorageItemsAsync(this IEnumerable<IStorageItem> items)
		{
			var newItems = new List<IStorageItem>();
			foreach (var item in items)
			{
				try
				{
					if (item is null)
					{
					}
					else if (item.IsOfType(StorageItemTypes.File))
					{
						newItems.Add(await item.AsBaseStorageFile()!.ToStorageFileAsync());
					}
					else if (item.IsOfType(StorageItemTypes.Folder))
					{
						newItems.Add(await item.AsBaseStorageFolder()!.ToStorageFolderAsync());
					}
				}
				catch (NotSupportedException)
				{
					// Ignore items that can't be converted
				}
			}
			return newItems;
		}

		[DynamicWindowsRuntimeCast(typeof(StorageFolder))]
		public static BaseStorageFolder? AsBaseStorageFolder(this IStorageItem? item)
		{
			if (item is not null && item.IsOfType(StorageItemTypes.Folder))
				return item is StorageFolder folder ? new SystemStorageFolder(folder) : item as BaseStorageFolder;

			return null;
		}

		public async static Task<BaseStorageFile?> DangerousGetFileFromPathAsync
			(string value, StorageFolderWithPath? rootFolder = null, StorageFolderWithPath? parentFolder = null)
				=> (await DangerousGetFileWithPathFromPathAsync(value, rootFolder, parentFolder)).Item;
		public async static Task<StorageFileWithPath> DangerousGetFileWithPathFromPathAsync
			(string value, StorageFolderWithPath? rootFolder = null, StorageFolderWithPath? parentFolder = null)
		{
			if (rootFolder is not null)
			{
				var rootItem = rootFolder.Item!;
				var currComponents = GetDirectoryPathComponents(value);

				if (parentFolder is not null && value.IsSubPathOf(parentFolder.Path))
				{
					var folder = parentFolder.Item!;
					var prevComponents = GetDirectoryPathComponents(parentFolder.Path);
					var path = parentFolder.Path;
					foreach (var component in currComponents.ExceptBy(prevComponents, c => c.Path).SkipLast(1))
					{
						folder = await folder.GetFolderAsync(component.Title ?? throw new InvalidOperationException("A path component is missing its title."));
						path = PathNormalization.Combine(path, folder.Name);
					}
					var file = await folder.GetFileAsync(currComponents.Last().Title ?? throw new InvalidOperationException("A path component is missing its title."));
					path = PathNormalization.Combine(path, file.Name);
					return new StorageFileWithPath(file, path);
				}
				else if (value.IsSubPathOf(rootFolder.Path))
				{
					var folder = rootItem;
					var path = rootFolder.Path;
					foreach (var component in currComponents.Skip(1).SkipLast(1))
					{
						folder = await folder.GetFolderAsync(component.Title ?? throw new InvalidOperationException("A path component is missing its title."));
						path = PathNormalization.Combine(path, folder.Name);
					}
					var file = await folder.GetFileAsync(currComponents.Last().Title ?? throw new InvalidOperationException("A path component is missing its title."));
					path = PathNormalization.Combine(path, file.Name);
					return new StorageFileWithPath(file, path);
				}
			}

			var fullPath = (parentFolder is not null && !FtpHelpers.IsFtpPath(value) && !Path.IsPathRooted(value) && !ShellStorageFolder.IsShellPath(value)) // "::{" not a valid root
				? Path.GetFullPath(Path.Combine(parentFolder.Path, value)) // Relative path
				: value;
			var item = await BaseStorageFile.GetFileFromPathAsync(fullPath);

			if (parentFolder is not null && parentFolder.Item is IPasswordProtectedItem ppis && item is IPasswordProtectedItem ppid)
				ppid.Credentials = ppis.Credentials;

			return new StorageFileWithPath(item);
		}
		public async static Task<IList<StorageFileWithPath>> GetFilesWithPathAsync
			(this StorageFolderWithPath parentFolder, uint maxNumberOfItems = uint.MaxValue)
				=> (await parentFolder.Item!.GetFilesAsync(CommonFileQuery.DefaultQuery, 0, maxNumberOfItems))
					.Select(x => new StorageFileWithPath(x, string.IsNullOrEmpty(x.Path) ? PathNormalization.Combine(parentFolder.Path, x.Name) : x.Path)).ToList();

		public async static Task<BaseStorageFolder?> DangerousGetFolderFromPathAsync
			(string value, StorageFolderWithPath? rootFolder = null, StorageFolderWithPath? parentFolder = null)
				=> (await DangerousGetFolderWithPathFromPathAsync(value, rootFolder, parentFolder)).Item;
		public async static Task<StorageFolderWithPath> DangerousGetFolderWithPathFromPathAsync
			(string value, StorageFolderWithPath? rootFolder = null, StorageFolderWithPath? parentFolder = null)
		{
			// Archive paths can't be resolved by chaining WinRT GetFolderAsync from a network root/parent
			// (an archive is not a real subfolder of the share); resolve them directly like local archives.
			if (rootFolder is not null && !ZipStorageFolder.IsZipPath(value))
			{
				var rootItem = rootFolder.Item!;
				var currComponents = GetDirectoryPathComponents(value);

				if (rootFolder.Path == value)
				{
					return rootFolder;
				}
				else if (parentFolder is not null && value.IsSubPathOf(parentFolder.Path))
				{
					var folder = parentFolder.Item!;
					var prevComponents = GetDirectoryPathComponents(parentFolder.Path);
					var path = parentFolder.Path;
					foreach (var component in currComponents.ExceptBy(prevComponents, c => c.Path))
					{
						folder = await folder.GetFolderAsync(component.Title ?? throw new InvalidOperationException("A path component is missing its title."));
						path = PathNormalization.Combine(path, folder.Name);
					}
					return new StorageFolderWithPath(folder, path);
				}
				else if (value.IsSubPathOf(rootFolder.Path))
				{
					var folder = rootItem;
					var path = rootFolder.Path;
					foreach (var component in currComponents.Skip(1))
					{
						folder = await folder.GetFolderAsync(component.Title ?? throw new InvalidOperationException("A path component is missing its title."));
						path = PathNormalization.Combine(path, folder.Name);
					}
					return new StorageFolderWithPath(folder, path);
				}
			}

			var fullPath = (parentFolder is not null && !FtpHelpers.IsFtpPath(value) && !Path.IsPathRooted(value) && !ShellStorageFolder.IsShellPath(value)) // "::{" not a valid root
				? Path.GetFullPath(Path.Combine(parentFolder.Path, value)) // Relative path
				: value;
			var item = await BaseStorageFolder.GetFolderFromPathAsync(fullPath);

			if (parentFolder is not null && parentFolder.Item is IPasswordProtectedItem ppis && item is IPasswordProtectedItem ppid)
				ppid.Credentials = ppis.Credentials;

			return new StorageFolderWithPath(item);
		}
		public async static Task<IList<StorageFolderWithPath>> GetFoldersWithPathAsync
			(this StorageFolderWithPath parentFolder, uint maxNumberOfItems = uint.MaxValue)
				=> (await parentFolder.Item!.GetFoldersAsync(CommonFolderQuery.DefaultQuery, 0, maxNumberOfItems))
					.Select(x => new StorageFolderWithPath(x, string.IsNullOrEmpty(x.Path) ? PathNormalization.Combine(parentFolder.Path, x.Name) : x.Path)).ToList();
		public async static Task<IList<StorageFolderWithPath>?> GetFoldersWithPathAsync
			(this StorageFolderWithPath parentFolder, string nameFilter, uint maxNumberOfItems = uint.MaxValue)
		{
			if (parentFolder is null)
				return null;

			var folder = parentFolder.Item!;

			var queryOptions = new QueryOptions
			{
				ApplicationSearchFilter = $"System.FileName:{nameFilter}*"
			};
			BaseStorageFolderQueryResult queryResult = folder.CreateFolderQueryWithOptions(queryOptions);

			return (await queryResult.GetFoldersAsync(0, maxNumberOfItems))
				.Select(x => new StorageFolderWithPath(x, string.IsNullOrEmpty(x.Path) ? PathNormalization.Combine(parentFolder.Path, x.Name) : x.Path)).ToList();
		}

	}
}
