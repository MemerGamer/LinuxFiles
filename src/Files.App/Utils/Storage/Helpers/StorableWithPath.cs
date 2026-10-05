// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.Storage;
using IO = System.IO;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// An <see cref="IStorageItemWithPath"/> backed by an optional <see cref="IStorable"/> instead of a WinRT storage item.
	/// </summary>
	/// <param name="Path">The full path of the item.</param>
	/// <param name="ItemType">Whether the item is a file or a directory.</param>
	/// <param name="Storable">The resolved storable, or <see langword="null"/> if it has not been resolved.</param>
	public sealed record StorableWithPath(string Path, FilesystemItemType ItemType, IStorable? Storable = null) : IStorageItemWithPath
	{
		/// <inheritdoc/>
		public string Name => IO.Path.GetFileName(IO.Path.TrimEndingDirectorySeparator(Path));

		IStorageItem? IStorageItemWithPath.Item => null;
	}
}
