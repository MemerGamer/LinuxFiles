// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using OwlCore.Storage;
using Windows.Storage;
using IO = System.IO;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// An <see cref="IStorageItemWithPath"/> backed by an optional <see cref="IStorable"/> instead of a WinRT storage item.
	/// </summary>
	public sealed record StorableWithPath : IStorageItemWithPath
	{
		/// <inheritdoc/>
		public string Path { get; }

		/// <inheritdoc/>
		public FilesystemItemType ItemType { get; }

		/// <inheritdoc/>
		public IStorable? Storable { get; }

		/// <summary>
		/// Gets the display name: the storable's name if one is attached; otherwise, the last segment of <see cref="Path"/>.
		/// </summary>
		public string Name => Storable?.Name ?? IO.Path.GetFileName(IO.Path.TrimEndingDirectorySeparator(Path));

		IStorageItem? IStorageItemWithPath.Item => null;

		/// <summary>
		/// Initializes a new instance of the <see cref="StorableWithPath"/> record.
		/// </summary>
		/// <param name="path">The full path of the item.</param>
		/// <param name="itemType">The kind of the item.</param>
		/// <param name="storable">The resolved storable, or <see langword="null"/> if it has not been resolved.</param>
		/// <exception cref="ArgumentException"><paramref name="path"/> is empty, or <paramref name="storable"/> contradicts <paramref name="itemType"/>.</exception>
		public StorableWithPath(string path, FilesystemItemType itemType, IStorable? storable = null)
		{
			ArgumentException.ThrowIfNullOrEmpty(path);

			// Symlinks may be backed by either kind
			var mismatch = storable switch
			{
				IFolder => itemType is FilesystemItemType.File,
				IFile => itemType is FilesystemItemType.Directory or FilesystemItemType.Library,
				_ => false,
			};
			if (mismatch)
				throw new ArgumentException($"A {storable!.GetType().Name} cannot back an item of type {itemType}.", nameof(storable));

			Path = path;
			ItemType = itemType;
			Storable = storable;
		}

		/// <summary>
		/// Creates a <see cref="StorableWithPath"/> whose <see cref="ItemType"/> follows the kind of <paramref name="storable"/>.
		/// </summary>
		/// <param name="path">The full path of the item.</param>
		/// <param name="storable">The resolved storable.</param>
		/// <returns>A new <see cref="StorableWithPath"/>.</returns>
		public static StorableWithPath FromStorable(string path, IStorable storable)
		{
			ArgumentNullException.ThrowIfNull(storable);
			return new(path, storable is IFolder ? FilesystemItemType.Directory : FilesystemItemType.File, storable);
		}
	}
}
