// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Core.Storage.Contracts;

namespace Files.App.Helpers
{
	public static partial class StorageHelpers
	{
		public static async Task<FilesystemResult<IStorable>> GetStorableAsync(string path, CancellationToken cancellationToken = default)
		{
			var result = await Ioc.Default.GetRequiredService<IStorableResolver>().TryGetAsync(path, cancellationToken);
			return new(result.Item, result.Status.ToFileSystemStatusCode());
		}

		public static async Task<FilesystemResult<IFile>> GetFileAsync(string path, CancellationToken cancellationToken = default)
		{
			var result = await GetStorableAsync(path, cancellationToken);
			return new(result.Result as IFile, result && result.Result is not IFile ? FileSystemStatusCode.NotAFile : result.ErrorCode);
		}

		public static async Task<FilesystemResult<IFolder>> GetFolderAsync(string path, CancellationToken cancellationToken = default)
		{
			var result = await GetStorableAsync(path, cancellationToken);
			return new(result.Result as IFolder, result && result.Result is not IFolder ? FileSystemStatusCode.NotAFolder : result.ErrorCode);
		}

		public static async Task<FilesystemResult<IStorable>> ToStorableResult(this IStorageItemWithPath item, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var result = item.Storable is { } storable
				? new FilesystemResult<IStorable>(storable, FileSystemStatusCode.Success)
				: await GetStorableAsync(item.Path, cancellationToken);
			if (result && item.ItemType is FilesystemItemType.File && result.Result is not IFile)
				return new(null, FileSystemStatusCode.NotAFile);
			if (result && item.ItemType is FilesystemItemType.Directory or FilesystemItemType.Library && result.Result is not IFolder)
				return new(null, FileSystemStatusCode.NotAFolder);
			return result;
		}

		/// <summary>
		/// Gets whether the item is a member of an archive; such items can only be copied out.
		/// </summary>
		public static bool IsArchiveMember(this IStorageItemWithPath item)
#if WINDOWS
			=> item.Item is ZipStorageFile || item.Item is ZipStorageFolder;
#else
			=> item.Storable is Files.App.Storage.Archives.ArchiveEntryFile or Files.App.Storage.Archives.ArchiveFolder
				|| Files.Shared.Helpers.FileExtensionHelpers.IsZipPath(item.Path, includeRoot: false);
#endif
	}
}
