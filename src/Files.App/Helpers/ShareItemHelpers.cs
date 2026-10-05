// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation.Metadata;
using Windows.Storage;

namespace Files.App.Helpers
{
	public static partial class ShareItemHelpers
	{
		public static bool IsItemShareable(ListedItem item)
			=> !item.IsHiddenItem &&
				(!item.IsShortcut || item.IsLinkItem) &&
				(item.PrimaryItemAttribute != StorageItemTypes.Folder || item.IsArchive);

		public static bool IsSupported()
#if WINDOWS
			=> DataTransferManager.IsSupported();
#else
			=> false; // LINUX-TODO(share): Share UI is hidden on Linux; future work is an xdg-desktop-portal backend behind Files.Platform.Abstractions
#endif

#if !WINDOWS
		public static Task ShareItemsAsync(IEnumerable<ListedItem> itemsToShare)
			=> Task.CompletedTask; // LINUX-TODO(share): see IsSupported
#endif
	}
}
