// Copyright (c) Files Community
// Licensed under the MIT License.

#if DESKTOP
using Files.App.UserControls.FilePreviews;
using Files.App.ViewModels.Properties;
using Files.Platform.Abstractions.Archives;
using System.Text;

namespace Files.App.ViewModels.Previews
{
	/// <summary>
	/// Previews an archive by listing its entries through <see cref="IArchiveService"/> (bounded; nothing is extracted).
	/// </summary>
	public static class ArchiveListingPreview
	{
		private const int MaxListedEntries = 500;

		public static bool IsArchive(ListedItem item)
			=> item.ItemPath is { } path && Ioc.Default.GetRequiredService<IArchiveService>().IsArchiveFileName(path);

		public static async Task<TextPreview?> TryLoadAsync(ListedItem item)
		{
			var service = Ioc.Default.GetRequiredService<IArchiveService>();

			try
			{
				using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
				var listing = await service.ListPreviewAsync(item.ItemPath!, cancellationToken: timeout.Token);

				var fileCount = 0;
				long totalSize = 0;
				foreach (var entry in listing.Entries)
				{
					if (!entry.IsDirectory)
					{
						fileCount++;
						totalSize = checked(totalSize + Math.Max(0, entry.Size));
					}
				}

				var folderCount = listing.Entries.Count - fileCount;

				var text = new StringBuilder();
				foreach (var entry in listing.Entries.Take(MaxListedEntries))
				{
					var size = entry.IsDirectory ? string.Empty : entry.Size.ToSizeString();
					text.Append(size.PadLeft(10)).Append("  ").Append(Sanitize(entry.Path.Length > 1024 ? entry.Path[..1024] + "…" : entry.Path)).Append(entry.IsDirectory ? "/" : string.Empty).Append('\n');
				}
				if (listing.Entries.Count > MaxListedEntries)
					text.Append("PreviewArchiveMoreEntries".GetLocalizedFormatResource(listing.Entries.Count - MaxListedEntries));

				var details = new List<FileProperty>
				{
					new() { NameResource = "PropertyItemCount", Value = Strings.DetailsArchiveItems.GetLocalizedFormatResource(listing.Entries.Count, fileCount, folderCount) },
					new() { NameResource = "PropertyUncompressedSize", Value = totalSize.ToSizeString() },
				};

				return await TextPreviewViewModel.CreateFromTextAsync(item, text.ToString(), details);
			}
			catch (Exception ex)
			{
				// Encrypted headers, unsupported formats and limit violations fall back to the generic preview.
				System.Diagnostics.Debug.WriteLine(ex);
				return null;
			}
		}

		// Entry names are untrusted: drop control characters so they cannot disturb the layout.
		private static string Sanitize(string name)
			=> string.Create(name.Length, name, static (span, source) =>
			{
				for (var i = 0; i < source.Length; i++)
					span[i] = char.IsControl(source[i]) ? '?' : source[i];
			});
	}
}
#endif
