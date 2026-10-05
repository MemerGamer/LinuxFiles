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

#if WINDOWS
		public IStorageItem? Item { get; }
#endif

		/// <summary>
		/// Gets the storable for this item, if one has been resolved; otherwise, <see langword="null"/>.
		/// </summary>
		/// <remarks>Implementations that predate storables return <see langword="null"/>.</remarks>
		public IStorable? Storable => null;

		public FilesystemItemType ItemType { get; }
	}
}
