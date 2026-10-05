// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using System.IO;
using Windows.Storage;

namespace Files.App.Utils.Storage
{
	public static partial class StorageFileExtensions
	{
		// LINUX-TODO(storage): retain legacy signatures until P4-B/E/G/H migrate to the resolver.
		public static BaseStorageFile? AsBaseStorageFile(this IStorageItem? item) => item as BaseStorageFile;
		public static BaseStorageFolder? AsBaseStorageFolder(this IStorageItem? item) => item as BaseStorageFolder;

		public static Task<List<IStorageItem>> ToStandardStorageItemsAsync(this IEnumerable<IStorageItem> items)
			=> Task.FromResult(items.ToList());

		public static async Task<BaseStorageFile?> DangerousGetFileFromPathAsync(string value, StorageFolderWithPath? rootFolder = null, StorageFolderWithPath? parentFolder = null)
		{
			var path = ResolveLegacyPath(value, parentFolder);
			var result = await StorageHelpers.GetFileAsync(path);
			ThrowLegacyResolutionError(result.ErrorCode, path);
			return null;
		}

		public static async Task<StorageFileWithPath> DangerousGetFileWithPathFromPathAsync(string value, StorageFolderWithPath? rootFolder = null, StorageFolderWithPath? parentFolder = null)
		{
			var path = ResolveLegacyPath(value, parentFolder);
			ThrowLegacyResolutionError((await StorageHelpers.GetFileAsync(path)).ErrorCode, path);
			return new(null, path);
		}

		public static async Task<BaseStorageFolder?> DangerousGetFolderFromPathAsync(string value, StorageFolderWithPath? rootFolder = null, StorageFolderWithPath? parentFolder = null)
		{
			var path = ResolveLegacyPath(value, parentFolder);
			var result = await StorageHelpers.GetFolderAsync(path);
			ThrowLegacyResolutionError(result.ErrorCode, path);
			return null;
		}

		public static async Task<StorageFolderWithPath> DangerousGetFolderWithPathFromPathAsync(string value, StorageFolderWithPath? rootFolder = null, StorageFolderWithPath? parentFolder = null)
		{
			var path = ResolveLegacyPath(value, parentFolder);
			ThrowLegacyResolutionError((await StorageHelpers.GetFolderAsync(path)).ErrorCode, path);
			return new(null, path);
		}

		private static string ResolveLegacyPath(string path, StorageFolderWithPath? parentFolder)
			=> parentFolder is not null && !Path.IsPathRooted(path) && !FtpHelpers.IsFtpPath(path)
				? Path.GetFullPath(Path.Combine(parentFolder.Path, path)) : path;

		private static void ThrowLegacyResolutionError(FileSystemStatusCode status, string path)
		{
			if (status is FileSystemStatusCode.NotFound)
				throw new FileNotFoundException("The storage item was not found.", path);
			if (status is FileSystemStatusCode.Unauthorized)
				throw new UnauthorizedAccessException("The storage item cannot be accessed.");
			if (status is FileSystemStatusCode.NotAFile or FileSystemStatusCode.NotAFolder)
				throw new ArgumentException("The storage item has a different kind.", nameof(path));
			throw new NotSupportedException("Use IStorableResolver instead of the legacy WinRT storage layer.");
		}

		public static Task<IList<StorageFileWithPath>> GetFilesWithPathAsync(this StorageFolderWithPath parentFolder, uint maxNumberOfItems = uint.MaxValue)
			=> throw new NotSupportedException("Use IFolder.GetItemsAsync instead of the legacy WinRT storage layer.");

		public static Task<IList<StorageFolderWithPath>> GetFoldersWithPathAsync(this StorageFolderWithPath parentFolder, uint maxNumberOfItems = uint.MaxValue)
			=> throw new NotSupportedException("Use IFolder.GetItemsAsync instead of the legacy WinRT storage layer.");

		public static Task<IList<StorageFolderWithPath>?> GetFoldersWithPathAsync(this StorageFolderWithPath parentFolder, string nameFilter, uint maxNumberOfItems = uint.MaxValue)
			=> throw new NotSupportedException("Use IFolder.GetItemsAsync instead of the legacy WinRT storage layer.");
	}
}
#endif
