// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.FileTags;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.Utils
{
	public partial class ListedItem
	{
		private long fileTagsRevision;
		public long FileTagsRevision => Interlocked.Read(ref fileTagsRevision);

		private string[]? fileTags;
		[DisallowNull]
		public string[]? FileTags
		{
			get => fileTags;
			set
			{
#if !WINDOWS
				if (fileTags is not null && fileTags.SequenceEqual(value, StringComparer.Ordinal))
					return;
#endif
				// fileTags is null when the item is first created
#if WINDOWS
				var fileTagsInitialized = fileTags is not null;
#else
				// Tags are loaded lazily, so an item that was never loaded counts as untagged and its first edit must still be saved
				const bool fileTagsInitialized = true;
#endif
				if (SetProperty(ref fileTags, value))
				{
					Interlocked.Increment(ref fileTagsRevision);
					// only set the tags if the file tags have been changed
					if (fileTagsInitialized)
					{
						var path = this.GetRequiredPath();
#if WINDOWS
						var dbInstance = FileTagsHelper.GetDbInstance();
						dbInstance.SetTags(path, FileFRN, value);
#endif
						_ = FileTagsHelper.WriteFileTagAsync(path, value);
					}

					HasTags = value is { Length: > 0 };
					OnPropertyChanged(nameof(FileTagsUI));
				}
			}
		}

		/// <summary>
		/// Applies tags read from storage; unlike <see cref="FileTags"/> it never writes them back.
		/// </summary>
		public void SetLoadedFileTags(string[] tags, long? expectedRevision = null)
		{
			if (expectedRevision is not null && expectedRevision != FileTagsRevision)
				return;

#if WINDOWS
			FileTags = tags;
#else
			// A deferred snapshot may predate an edit made through another item for this path.
			if (FileTagsHelper.TryReadDatabaseFallback(this.GetRequiredPath(), out var fallbackTags))
				tags = fallbackTags;

			if (fileTags is not null && fileTags.SequenceEqual(tags, StringComparer.Ordinal))
				return;

			if (SetProperty(ref fileTags, tags, nameof(FileTags)))
			{
				HasTags = tags.Length > 0;
				OnPropertyChanged(nameof(FileTagsUI));
			}
#endif
		}

		/// <summary>
		/// Applies an edit against the latest tags in the serialized storage queue.
		/// </summary>
		public async Task EditFileTagsAsync(Func<string[], string[]> edit)
		{
#if WINDOWS
			if (fileTags is null)
				SetLoadedFileTags(FileTagsHelper.ReadFileTag(this.GetRequiredPath()));
			FileTags = edit(fileTags ?? []);
			await Task.CompletedTask;
#else
			var revision = Interlocked.Increment(ref fileTagsRevision);
			var tags = await FileTagsHelper.EditFileTagsAsync(this.GetRequiredPath(), edit);
			if (tags is not null)
				SetLoadedFileTags(tags, revision);
#endif
		}
	}
}
