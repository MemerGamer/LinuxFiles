// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Files.Platform.Abstractions.Tags;
using Microsoft.Extensions.Logging;
using Windows.Storage;

namespace Files.App.Utils.FileTags
{
	// Linux: tags live in the user.xdg.tags extended attribute (tag names, comma separated) and in the app's own database.
	// Files stores tag UIDs internally, so names are translated through the tag list; xattr tags with an unknown name are ignored.
	public static partial class FileTagsHelper
	{
		public static string[] ReadFileTag(string filePath)
		{
			var store = Ioc.Default.GetService<IFileTagsStore>();
			var names = store?.ReadTags(filePath);

			if (names is { Count: > 0 })
			{
				var settings = Ioc.Default.GetRequiredService<IFileTagsSettingsService>();
				var uids = names
					.Select(name => settings.GetTagsByName(name).FirstOrDefault()?.Uid)
					.Where(uid => uid is not null)
					.Select(uid => uid!)
					.Distinct()
					.ToArray();

				if (uids.Length > 0)
					return uids;
			}

			// File systems without xattr support (and files whose tags were set elsewhere): fall back to the database
			return GetDbInstance().GetTags(filePath, null);
		}

		public static Task<bool> WriteFileTagAsync(string filePath, string[] tag, CancellationToken cancellationToken = default)
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();

				GetDbInstance().SetTags(filePath, null, tag);

				// Best effort: the database already holds the tags, so a file system without xattr support is not an error
				var store = Ioc.Default.GetService<IFileTagsStore>();
				if (store is not null)
				{
					var settings = Ioc.Default.GetRequiredService<IFileTagsSettingsService>();
					var names = settings.GetTagsByIds(tag)?.Select(x => x.Name).ToArray() ?? [];
					if (!store.WriteTags(filePath, names))
						App.Logger?.LogDebug("Extended attributes are not available for '{FilePath}'; tags are kept in the database only.", LogPathHelper.RedactPath(filePath));
				}

				return Task.FromResult(true);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return Task.FromResult(false);
			}
		}

		public static void UpdateTagsDb()
		{
			// Windows re-resolves moved files through their file reference numbers; Linux has none.
			// Entries of files that are missing are kept on purpose: the file may live on a drive that is not mounted right now.
		}

		public static ulong? GetFileFRN(string filePath) => null;
	}
}
#endif
