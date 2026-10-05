// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using Windows.Storage;

namespace Files.App.Utils
{
	public sealed partial class FtpItem
	{
		public async Task<IStorageItem> ToStorageItem()
		{
			var path = this.GetRequiredPath();
			var name = ItemNameRaw ?? throw new InvalidOperationException("The FTP item does not have a name.");

			return PrimaryItemAttribute switch
			{
				StorageItemTypes.File => await new Utils.Storage.FtpStorageFile(path, name, ItemDateCreatedReal).ToStorageFileAsync(),
				StorageItemTypes.Folder => new Utils.Storage.FtpStorageFolder(path, name, ItemDateCreatedReal),
				_ => throw new InvalidDataException("The FTP item has an unsupported storage type."),
			};
		}
	}
}
