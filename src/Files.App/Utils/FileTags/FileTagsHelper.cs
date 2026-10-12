// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation.Metadata;
using Windows.Storage;
using Windows.Storage.FileProperties;
using IO = System.IO;

namespace Files.App.Utils.FileTags
{

	public static partial class FileTagsHelper
	{
		private static readonly Lazy<FileTagsDatabase> dbInstance = new(() => new());

		public static FileTagsDatabase GetDbInstance() => dbInstance.Value;


		/// <summary>
		/// Prompts the user for confirmation, then removes all tags from the given items that have tags.
		/// </summary>
		/// <returns>True if the user confirmed and tags were removed; otherwise false.</returns>
		public static async Task<bool> RemoveTagsAsync(IEnumerable<ListedItem> items)
		{
			var itemsWithTags = items.Where(item => item.FileTags is { Length: > 0 }).ToList();
			if (itemsWithTags.Count == 0)
				return false;

			var confirmed = await DialogDisplayHelper.ShowDialogAsync(
				Strings.RemoveTags.GetLocalizedResource(),
				Strings.ConfirmRemoveTagsDialogContent.GetLocalizedResource(),
				Strings.Yes.GetLocalizedResource(),
				Strings.Cancel.GetLocalizedResource());

			if (!confirmed)
				return false;

			await Task.WhenAll(itemsWithTags.Select(item => item.EditFileTagsAsync(_ => [])));

			return true;
		}


		// File reference numbers do not exist on Linux; tags are keyed by path.
		public static Task<ulong?> GetFileFRN(IStorageItem? item) => Task.FromResult<ulong?>(null);
	}
}
