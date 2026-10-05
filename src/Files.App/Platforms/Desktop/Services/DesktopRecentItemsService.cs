// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Mime;
using Files.Platform.Abstractions.Recent;
using System.Collections.Specialized;
using System.IO;

namespace Files.App.Services.Desktop
{
	/// <summary>
	/// Recent files and folders backed by the shared <c>recently-used.xbel</c> (GTK/Qt applications read and write the same list).
	/// </summary>
	internal sealed class DesktopRecentItemsService : IWindowsRecentItemsService
	{
		private const int MaxItems = 20;

		private readonly IRecentFilesStore store;
		private readonly IMimeTypeService mimeTypeService;
		private readonly object gate = new();
		private List<RecentItem> recentFiles = [];
		private List<RecentItem> recentFolders = [];

		public DesktopRecentItemsService(IRecentFilesStore store, IMimeTypeService mimeTypeService)
		{
			this.store = store;
			this.mimeTypeService = mimeTypeService;
			store.Changed += (_, _) =>
			{
				_ = UpdateRecentFilesAsync();
				_ = UpdateRecentFoldersAsync();
			};
		}

		public IReadOnlyList<RecentItem> RecentFiles
		{
			get { lock (gate) return recentFiles.ToList().AsReadOnly(); }
		}

		public IReadOnlyList<RecentItem> RecentFolders
		{
			get { lock (gate) return recentFolders.ToList().AsReadOnly(); }
		}

		public event EventHandler<NotifyCollectionChangedEventArgs>? RecentFilesChanged;

		public event EventHandler<NotifyCollectionChangedEventArgs>? RecentFoldersChanged;

		public Task<bool> UpdateRecentFilesAsync() => Task.Run(() => Update(isFolder: false));

		public Task<bool> UpdateRecentFoldersAsync() => Task.Run(() => Update(isFolder: true));

		public bool Add(string path)
		{
			if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path))
				return false;

			string? mime = null;
			try
			{
				mime = Directory.Exists(path)
					? "inode/directory"
					: mimeTypeService.GetMimeTypeAsync(path).GetAwaiter().GetResult();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				// Recorded without a MIME type
			}

			return store.Add(path, mime);
		}

		public bool Remove(RecentItem item) => store.Remove(item.Path);

		public bool Clear() => store.Clear();

		private bool Update(bool isFolder)
		{
			try
			{
				var items = new List<RecentItem>();
				foreach (var entry in store.Read())
				{
					var isDirectory = Directory.Exists(entry.Path);
					if (isDirectory != isFolder || (!isDirectory && !File.Exists(entry.Path)))
						continue;

					var name = Path.GetFileName(entry.Path.TrimEnd('/'));
					items.Add(new RecentItem
					{
						Path = entry.Path,
						Name = string.IsNullOrEmpty(name) ? entry.Path : name,
						LastModified = entry.LastUsedUtc.ToLocalTime(),
					});

					if (items.Count == MaxItems)
						break;
				}

				lock (gate)
				{
					if (isFolder)
						recentFolders = items;
					else
						recentFiles = items;
				}

				// The widgets refresh everything on Reset
				var args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
				if (isFolder)
					RecentFoldersChanged?.Invoke(this, args);
				else
					RecentFilesChanged?.Invoke(this, args);

				return true;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return false;
			}
		}
	}
}
