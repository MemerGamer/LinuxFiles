// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Files.App.ViewModels.Properties;
using Files.Platform.Abstractions.Archives;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class ArchivePreviewViewModel(ListedItem item) : BasePreviewModel(item)
	{
		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var listing = await Ioc.Default.GetRequiredService<IArchiveService>().ListPreviewAsync(item.ItemPath!);
			var files = listing.Entries.Count(entry => !entry.IsDirectory);
			var total = listing.Entries.Aggregate(0UL, (sum, entry) => checked(sum + (ulong)entry.Size));
			var details = new List<FileProperty>
			{
				GetFileProperty("PropertyItemCount", Strings.DetailsArchiveItems.GetLocalizedFormatResource(listing.Entries.Count, files, listing.Entries.Count - files)),
				GetFileProperty("PropertyUncompressedSize", total.ToLongSizeString())
			};
			_ = await base.LoadPreviewAndDetailsAsync();
			return details;
		}
	}
}
#endif
