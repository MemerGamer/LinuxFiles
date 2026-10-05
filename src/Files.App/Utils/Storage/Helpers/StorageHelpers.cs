// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Core.Storage.Contracts;
using Windows.Storage;

namespace Files.App.Helpers
{
	public static partial class StorageHelpers
	{
#if !WINDOWS
		// LINUX-TODO(storage): remove these WinRT entry points as downstream work packages adopt ToStorableResult.
		public static async Task<IStorageItem?> ToStorageItem(this IStorageItemWithPath item)
			=> (await item.ToStorageItemResult()).Result;

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

		public static async Task<FilesystemResult<IStorageItem>> ToStorageItemResult(this IStorageItemWithPath item)
		{
			var result = await item.ToStorableResult();
			if (!result)
				return new(null, result.ErrorCode);

			IStorageItem? legacy = item.Item
				?? (result.Result is IFolder
					? await StorageFileExtensions.OpenLegacyFolderAsync(item.Path)
					: await StorageFileExtensions.OpenLegacyFileAsync(item.Path));
			return new(legacy, legacy is null ? FileSystemStatusCode.Generic : FileSystemStatusCode.Success);
		}

		public static async Task<long> GetFileSize(this IStorageFile file)
			=> (long)(await file.GetBasicPropertiesAsync()).Size;

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

		public static FilesystemResult<T> ToType<T, V>(FilesystemResult<V> result) where T : class
			=> new(result.Result as T, result.ErrorCode);
#endif
	}
}
