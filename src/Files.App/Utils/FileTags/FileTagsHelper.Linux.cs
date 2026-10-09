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

		private static readonly object writeGate = new();
		private static Task pendingWrite = Task.CompletedTask;

		public static Task<bool> WriteFileTagAsync(string filePath, string[] tag, CancellationToken cancellationToken = default)
		{
			if (cancellationToken.IsCancellationRequested) return Task.FromResult(false);
			var tags = (string[])tag.Clone();
			var store = Ioc.Default.GetService<IFileTagsStore>();
			var settings = Ioc.Default.GetRequiredService<IFileTagsSettingsService>();
			var names = settings.GetTagsByIds(tags)?.Select(x => x.Name).ToArray() ?? [];
			lock (writeGate)
			{
				var previous = pendingWrite;
				var write = Task.Run(async () =>
				{
					await previous.ConfigureAwait(false);
					if (cancellationToken.IsCancellationRequested) return false;
					try
					{
						GetDbInstance().SetTags(filePath, null, tags);
						if (store is not null && !store.WriteTags(filePath, names))
							App.Logger?.LogDebug("Extended attributes are not available for '{FilePath}'; tags are kept in the database only.", LogPathHelper.RedactPath(filePath));
						return true;
					}
					catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
					{
						App.Logger?.LogWarning(ex, "Could not write file tags.");
						return false;
					}
				});
				pendingWrite = write;
				return write;
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
