// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Enumeration;
using Files.Platform.Abstractions.Watching;
using Microsoft.Extensions.Logging;
using System.IO;
using Windows.Storage;
using FileAttributes = System.IO.FileAttributes;

namespace Files.App.ViewModels
{
	/// <summary>
	/// Linux folder listing and change watching on top of <see cref="IFileSystemEnumerator"/> / <see cref="IFolderWatcherFactory"/>,
	/// replacing the FindFirstFileEx / ReadDirectoryChangesW paths of <see cref="ShellViewModel"/>.
	/// </summary>
	public sealed partial class ShellViewModel
	{
		private Files.Platform.Abstractions.Watching.IFolderWatcher? _linuxWatcher;
		private CancellationTokenSource? _linuxRefreshDebounce;
		private readonly List<Files.Platform.Abstractions.Watching.IFolderWatcher> _linuxRepositoryWatchers = [];

		/// <summary>
		/// Lists <paramref name="path"/> into <c>filesAndFolders</c>. Returns 3 on success and -1 on failure.
		/// </summary>
		private async Task<int> EnumerateLinuxFolderAsync(string path, CancellationToken cancellationToken, LibraryItem? library)
		{
			if (!Directory.Exists(path))
			{
				ShowLocationInaccessibleOrMissing(path);
				return -1;
			}

			currentStorageFolder = null;
			HasNoWatcher = false;

			var info = new DirectoryInfo(path);
			var currentFolder = library ?? new ListedItem(null)
			{
				PrimaryItemAttribute = StorageItemTypes.Folder,
				ItemPropertiesInitialized = true,
				ItemNameRaw = string.IsNullOrEmpty(info.Name) ? path : info.Name,
				ItemDateModifiedReal = info.LastWriteTime,
				ItemDateCreatedReal = info.CreationTime,
				ItemType = folderTypeTextLocalized,
				FileImage = null,
				LoadFileIcon = false,
				ItemPath = path,
				FileSize = null,
				FileSizeBytes = 0,
			};

			CurrentFolder = currentFolder;

			// Probe access up front so an unreadable folder shows the standard message
			try
			{
				using var probe = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
				probe.MoveNext();
			}
			catch (UnauthorizedAccessException)
			{
				ShowLocationInaccessibleOrMissing(path);
				return -1;
			}
			catch (IOException)
			{
				ShowLocationUnavailable(LocationUnavailableKind.DriveUnplugged);
				return -1;
			}

			var enumerator = Ioc.Default.GetRequiredService<IFileSystemEnumerator>();
			var iconCache = Ioc.Default.GetRequiredService<IIconCacheService>();
			var folders = Ioc.Default.GetRequiredService<IUserSettingsService>().FoldersSettingsService;
			var includeHidden = folders.ShowHiddenItems || folders.ShowDotFiles;
			var iconSize = GetPreloadIconSize();

			await Task.Run(async () =>
			{
				var pending = new List<ListedItem>();
				var firstFlushDone = false;
				var lastFlush = Stopwatch.StartNew();

				await foreach (var entry in enumerator.EnumerateAsync(path, new FileSystemEnumerationOptions { IncludeHidden = includeHidden }, cancellationToken))
				{
					if (cancellationToken.IsCancellationRequested)
						break;

					if (entry.IsHidden && !includeHidden)
						continue;

					var item = CreateLinuxListedItem(entry);
					try { item.PreloadedIconData = await iconCache.GetIconAsync(item.ItemPath, item.FileExtension, entry.IsDirectory, iconSize); } catch (Exception ex) { App.Logger.LogWarning(ex, "Could not load icon for {Path}", item.ItemPath); }
					pending.Add(item);

					if ((!firstFlushDone && pending.Count >= 25) || (firstFlushDone && lastFlush.ElapsedMilliseconds > 500))
					{
						firstFlushDone = true;
						lastFlush.Restart();
						filesAndFolders.AddRange(pending);
						pending.Clear();

						if (filesAndFolders.Count <= 10_000)
							await OrderFilesAndFoldersAsync();

						await ApplyFilesAndFoldersChangesAsync();
					}
				}

				filesAndFolders.AddRange(pending);

				await OrderFilesAndFoldersAsync();
				await ApplyFilesAndFoldersChangesAsync();

				// Uno does not raise the container update callbacks that normally trigger this, so the status columns are filled in here
				_ = LoadLinuxRepositoryPropertiesAsync(cancellationToken);

				// desktop.ini based customization has no Linux equivalent; the Windows services are stubs
				_ = dispatcherQueue.EnqueueOrInvokeAsync(CheckForSolutionFile, Microsoft.UI.Dispatching.DispatcherQueuePriority.Low);
				desktopIniUpdateTask = dispatcherQueue.EnqueueOrInvokeAsync(() =>
				{
					GetDesktopIniFileData();
					CheckForBackgroundImage();
				}, Microsoft.UI.Dispatching.DispatcherQueuePriority.Low);
			}, cancellationToken);

			IsLocationUnavailable = false;
			return 3;
		}

		private async Task LoadLinuxRepositoryPropertiesAsync(CancellationToken cancellationToken)
		{
			if (!IsValidGitDirectory || EnabledGitProperties is GitProperties.None)
				return;

			try
			{
				// The first screens only; the rest loads when selected or when the layout reloads its items
				var items = filesAndFolders.OfType<IGitItem>().Take(300).ToList();
				foreach (var gitItem in items)
				{
					cancellationToken.ThrowIfCancellationRequested();
					await LoadGitPropertiesAsync(gitItem);
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		// Items inside a repository use the Git item type so the status/commit columns can be filled in on demand
		private ListedItem NewLinuxItem() => IsValidGitDirectory ? new GitItem() : new ListedItem(null);

		private ListedItem CreateLinuxListedItem(FileSystemEntryInfo entry)
		{
			var opacity = entry.IsHidden ? Constants.UI.DimItemOpacity : 1d;
			var modified = entry.LastWriteTimeUtc.ToLocalTime();
			var created = (entry.CreationTimeUtc == default ? entry.LastWriteTimeUtc : entry.CreationTimeUtc).ToLocalTime();
			var accessed = entry.LastAccessTimeUtc.ToLocalTime();

			if (entry.IsDirectory)
			{
				var folder = NewLinuxItem();
				folder.PrimaryItemAttribute = StorageItemTypes.Folder;
				folder.ItemNameRaw = entry.Name;
				folder.ItemDateModifiedReal = modified;
				folder.ItemDateCreatedReal = created;
				folder.ItemType = folderTypeTextLocalized;
				folder.FileImage = null;
				folder.IsHiddenItem = entry.IsHidden;
				folder.Opacity = opacity;
				folder.LoadFileIcon = false;
				folder.ItemPath = entry.FullPath;
				folder.FileSize = null;
				folder.FileSizeBytes = 0;
				return folder;
			}

			// LINUX-TODO(shortcuts): symlinks and .desktop files are listed as plain files
			string itemType = Strings.File.GetLocalizedResource();
			string? extension = null;
			if (entry.Name.Contains('.'))
			{
				extension = Path.GetExtension(entry.Name);
				var localizedType = FileTypesHelper.GetLocalizedTypeName(extension);
				itemType = !string.IsNullOrEmpty(localizedType) ? localizedType : extension.Trim('.') + " " + itemType;
			}

			var item = NewLinuxItem();
			item.PrimaryItemAttribute = StorageItemTypes.File;
			item.FileExtension = extension;
			item.IsHiddenItem = entry.IsHidden;
			item.Opacity = opacity;
			item.FileImage = null;
			item.LoadFileIcon = false;
			item.ItemNameRaw = entry.Name;
			item.ItemDateModifiedReal = modified;
			item.ItemDateAccessedReal = accessed;
			item.ItemDateCreatedReal = created;
			item.ItemType = itemType;
			item.ItemPath = entry.FullPath;
			item.FileSize = entry.Length.ToSizeString();
			item.FileSizeBytes = entry.Length;
			return item;
		}

		private void WatchForLinuxFolderChanges(string path)
		{
			if (isDisposed)
				return;

			CloseLinuxWatcher();

			try
			{
				var watcherInstance = Ioc.Default.GetRequiredService<IFolderWatcherFactory>().Create(path, new FolderWatcherOptions { Debounce = TimeSpan.FromMilliseconds(250) });

				void OnChanged(object? s, EventArgs e) => _ = RefreshAfterLinuxChangeAsync();

				watcherInstance.Created += OnChanged;
				watcherInstance.Deleted += OnChanged;
				watcherInstance.Changed += OnChanged;
				watcherInstance.Renamed += OnChanged;
				watcherInstance.RescanRequired += OnChanged;
				watcherInstance.Start();

				_linuxWatcher = watcherInstance;
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Could not watch {Path}", path);
			}
		}

		// LINUX-TODO(watcher): refreshes the whole listing; map Created/Deleted/Renamed onto the incremental AddFileOrFolder/RemoveFileOrFolder paths
		private async Task RefreshAfterLinuxChangeAsync()
		{
			_linuxRefreshDebounce?.Cancel();
			var cts = _linuxRefreshDebounce = new CancellationTokenSource();

			try
			{
				await Task.Delay(300, cts.Token);
				await dispatcherQueue.EnqueueOrInvokeAsync(() => RefreshItems(null));
			}
			catch (OperationCanceledException)
			{
			}
		}

		// Raises GitDirectoryUpdated when the repository metadata changes (commit, checkout, stage, fetch) so the status columns refresh
		private void WatchForLinuxRepositoryChanges()
		{
			if (isDisposed || string.IsNullOrEmpty(GitDirectory))
				return;

			DisposeLinuxRepositoryWatchers();

			// .git may be a gitfile (linked worktree, submodule); HEAD and the index live in the git dir, refs in the common dir
			if (Files.Platform.Linux.Search.GitDirectoryResolver.Resolve(GitDirectory) is not var (gitDir, commonDir))
				return;

			try
			{
				var factory = Ioc.Default.GetRequiredService<IFolderWatcherFactory>();
				var paths = string.Equals(gitDir, commonDir, StringComparison.Ordinal) ? [gitDir] : new[] { gitDir, commonDir };

				void OnChanged(object? s, EventArgs e) => _ = dispatcherQueue.EnqueueOrInvokeAsync(async () =>
				{
					await ReloadLinuxGitPropertiesAsync();
					GitDirectoryUpdated?.Invoke(null, null!);
				});

				foreach (var path in paths)
				{
					var watcherInstance = factory.Create(path, new FolderWatcherOptions { Debounce = TimeSpan.FromMilliseconds(400) });
					watcherInstance.Created += OnChanged;
					watcherInstance.Deleted += OnChanged;
					watcherInstance.Changed += OnChanged;
					watcherInstance.Renamed += OnChanged;
					watcherInstance.Start();
					_linuxRepositoryWatchers.Add(watcherInstance);
				}
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Could not watch the repository metadata of {Path}", GitDirectory);
			}
		}

		// The Git columns are loaded once per item; after the repository changed they are stale, so reset the flags and reload
		private async Task ReloadLinuxGitPropertiesAsync()
		{
			if (isDisposed || !IsValidGitDirectory)
				return;

			var items = filesAndFolders.OfType<IGitItem>().ToList();
			foreach (var item in items)
			{
				item.StatusPropertiesInitialized = false;
				item.CommitPropertiesInitialized = false;
			}

			try
			{
				foreach (var item in items.Take(300))
					await LoadGitPropertiesAsync(item);
			}
			catch (Exception ex) when (ex is not OutOfMemoryException)
			{
				App.Logger.LogWarning(ex, "Could not reload the Git columns of {Path}", GitDirectory);
			}
		}

		private void DisposeLinuxRepositoryWatchers()
		{
			foreach (var w in _linuxRepositoryWatchers)
				w.Dispose();
			_linuxRepositoryWatchers.Clear();
		}

		private void CloseLinuxWatcher()
		{
			DisposeLinuxRepositoryWatchers();
			_linuxRefreshDebounce?.Cancel();
			_linuxWatcher?.Dispose();
			_linuxWatcher = null;
		}
	}
}
