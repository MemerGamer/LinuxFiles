// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Core.Storage.Contracts;
using Windows.Storage;

namespace Files.App.Helpers
{
	public static partial class StorageHelpers
	{
#if !WINDOWS
		// LINUX-TODO(storage): remove the remaining WinRT entry points (ToStorageItem<T>, FromStorageItem) once their callers use storables.
		public static async Task<TRequested?> ToStorageItem<TRequested>(string path) where TRequested : IStorageItem
		{
			var result = await GetStorableAsync(path);
			if (!result)
				return default;

			IStorageItem? item = result.Result is IFolder
				? await StorageFileExtensions.OpenLegacyFolderAsync(path)
				: await StorageFileExtensions.OpenLegacyFileAsync(path);
			return item is TRequested requested ? requested : default;
		}

		public static IStorageItemWithPath FromPathAndType(string customPath, FilesystemItemType? itemType)
			=> new StorableWithPath(customPath, itemType ?? FilesystemItemType.Directory);

		public static async Task<FilesystemItemType> GetTypeFromPath(string path)
			=> (await GetStorableAsync(path)).Result is IFolder ? FilesystemItemType.Directory : FilesystemItemType.File;

		public static bool Exists(string path)
			=> System.IO.Directory.Exists(path) || System.IO.File.Exists(path);

		public static IStorageItemWithPath? FromStorageItem(this IStorageItem? item, string? customPath = null, FilesystemItemType? itemType = null)
			=> FromPathAndType(!string.IsNullOrEmpty(item?.Path) ? item.Path : customPath
				?? throw new InvalidOperationException("A path is required when converting a missing storage item."),
				item is null ? itemType : item.IsOfType(StorageItemTypes.Folder) ? FilesystemItemType.Directory : FilesystemItemType.File);
#endif
	}
}
