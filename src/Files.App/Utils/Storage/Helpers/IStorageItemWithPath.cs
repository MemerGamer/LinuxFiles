// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using OwlCore.Storage;
using Windows.Storage;

namespace Files.App.Utils.Storage
{
	public interface IStorageItemWithPath
	{
		public string Name { get; }

		public string Path { get; }

		// LINUX-TODO(storage): move under #if WINDOWS in P4-Z; desktop consumers (e.g. StorageHelpers.ToStorageItemResult) still read it
		public IStorageItem? Item { get; }

		/// <summary>
		/// Gets the storable for this item, if one has been resolved; otherwise, <see langword="null"/>.
		/// </summary>
		/// <remarks>Implementations that predate storables return <see langword="null"/>.</remarks>
		public IStorable? Storable => null;

		public FilesystemItemType ItemType { get; }
	}
}
