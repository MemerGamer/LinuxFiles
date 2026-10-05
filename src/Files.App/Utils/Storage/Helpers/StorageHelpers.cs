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
			await GetStorableAsync(path);
			return default;
		}

		public static async Task<FilesystemResult<IStorageItem>> ToStorageItemResult(this IStorageItemWithPath item)
		{
			var result = await item.ToStorableResult();
			return new(null, result ? FileSystemStatusCode.Generic : result.ErrorCode);
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
