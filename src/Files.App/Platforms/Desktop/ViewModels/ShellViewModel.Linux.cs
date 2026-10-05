// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Enumeration;
using Files.Platform.Abstractions.Trash;
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
		private Action? _linuxTrashUnsubscribe;
		private CancellationTokenSource? _linuxGitDebounce;

		/// <summary>
		/// Lists <paramref name="path"/> into <c>filesAndFolders</c>. Returns 3 on success and -1 on failure.
		/// </summary>
		private async Task<int> EnumerateLinuxFolderAsync(string path, CancellationToken cancellationToken, LibraryItem? library)
		{
			if (path.StartsWith(Constants.UserEnvironmentPaths.RecycleBinPath, StringComparison.Ordinal))
				return await EnumerateLinuxTrashAsync(path, cancellationToken);

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

				// A superseded navigation must not publish a partial listing
				if (cancellationToken.IsCancellationRequested)
					return;

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

			if (cancellationToken.IsCancellationRequested)
				return -1;

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

		/// <summary>
		/// Lists the freedesktop.org trash (<c>trash:///</c>) through <see cref="IStorageTrashBinService"/>. Returns 4 on success.
		/// </summary>
		private async Task<int> EnumerateLinuxTrashAsync(string path, CancellationToken cancellationToken)
		{
			currentStorageFolder = null;
			HasNoWatcher = false;

			CurrentFolder = new ListedItem(null)
			{
				PrimaryItemAttribute = StorageItemTypes.Folder,
				ItemPropertiesInitialized = true,
				ItemNameRaw = Strings.RecycleBin.GetLocalizedResource(),
				ItemType = folderTypeTextLocalized,
				FileImage = null,
				LoadFileIcon = false,
				ItemPath = Constants.UserEnvironmentPaths.RecycleBinPath,
				FileSize = null,
				FileSizeBytes = 0,
			};

			var trashed = await StorageTrashBinService.GetAllRecycleBinFoldersAsync();
			var iconCache = Ioc.Default.GetRequiredService<IIconCacheService>();
			var iconSize = GetPreloadIconSize();
			var items = new List<ListedItem>(trashed.Count);

			foreach (var entry in trashed)
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				var name = entry.FileName ?? Path.GetFileName(entry.RecyclePath) ?? string.Empty;
				var extension = entry.IsFolder || !name.Contains('.') ? null : Path.GetExtension(name);
				var itemType = entry.IsFolder ? folderTypeTextLocalized : Strings.File.GetLocalizedResource();
				if (extension is not null)
				{
					var localizedType = FileTypesHelper.GetLocalizedTypeName(extension);
					itemType = !string.IsNullOrEmpty(localizedType) ? localizedType : extension.Trim('.') + " " + itemType;
				}

				var item = new RecycleBinItem(null)
				{
					PrimaryItemAttribute = entry.IsFolder ? StorageItemTypes.Folder : StorageItemTypes.File,
					FileExtension = extension,
					ItemNameRaw = name,
					ItemPath = entry.RecyclePath ?? string.Empty,
					ItemOriginalPath = entry.FilePath,
					ItemDateDeletedReal = entry.RecycleDate,
					ItemDateModifiedReal = entry.ModifiedDate,
					ItemDateCreatedReal = entry.CreatedDate,
					ItemType = itemType,
					FileImage = null,
					LoadFileIcon = false,
					Opacity = 1d,
					FileSize = entry.IsFolder ? null : entry.FileSize,
					FileSizeBytes = (long)entry.FileSizeBytes,
				};

				try { item.PreloadedIconData = await iconCache.GetIconAsync(item.ItemPath, item.FileExtension, entry.IsFolder, iconSize); }
				catch (Exception ex) { App.Logger.LogWarning(ex, "Could not load icon for {Path}", item.ItemPath); }

				items.Add(item);
			}

			if (cancellationToken.IsCancellationRequested)
				return -1;

			filesAndFolders.Clear();
			filesAndFolders.AddRange(items);

			await OrderFilesAndFoldersAsync();
			await ApplyFilesAndFoldersChangesAsync();

			IsLocationUnavailable = false;
			return 4;
		}

		// Trash changes are pushed by ITrashService.Watcher rather than by a folder watcher on trash:///
		private void WatchForLinuxTrashChanges()
		{
			if (isDisposed)
				return;

			CloseLinuxWatcher();

			try
			{
				var notifier = Ioc.Default.GetRequiredService<ITrashService>().Watcher;
				void OnChanged(object? s, EventArgs e) => _ = RefreshAfterLinuxChangeAsync();

				notifier.ItemAdded += OnChanged;
				notifier.ItemDeleted += OnChanged;
				notifier.ItemChanged += OnChanged;
				notifier.ItemRenamed += OnChanged;
				notifier.RefreshRequested += OnChanged;
				notifier.StartWatcher();

				_linuxTrashUnsubscribe = () =>
				{
					notifier.ItemAdded -= OnChanged;
					notifier.ItemDeleted -= OnChanged;
					notifier.ItemChanged -= OnChanged;
					notifier.ItemRenamed -= OnChanged;
					notifier.RefreshRequested -= OnChanged;
				};
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Could not watch the trash");
			}
		}

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

				// HEAD/index/packed-refs live directly in the git and common dirs; branch tips and their reflogs are nested in
				// refs/** and logs/**. objects/ is deliberately not watched.
				var targets = new List<(string Path, bool Recursive)>
				{
					(gitDir, false),
					(Path.Combine(gitDir, "logs"), true),
					(commonDir, false),
					(Path.Combine(commonDir, "refs"), true),
					(Path.Combine(commonDir, "logs"), true),
				};

				void OnChanged(object? s, FolderChangeEventArgs e)
				{
					// Lock files appear and vanish around every Git write; the final rename is what matters
					if (e.Name.EndsWith(".lock", StringComparison.Ordinal) || e.FullPath.Contains("/objects/", StringComparison.Ordinal))
						return;

					ScheduleLinuxGitRefresh();
				}

				foreach (var (path, recursive) in targets.DistinctBy(t => t.Path))
				{
					if (!Directory.Exists(path))
						continue;

					var watcherInstance = factory.Create(path, new FolderWatcherOptions { IncludeSubdirectories = recursive, Debounce = TimeSpan.FromMilliseconds(200) });
					watcherInstance.Created += OnChanged;
					watcherInstance.Deleted += OnChanged;
					watcherInstance.Changed += OnChanged;
					watcherInstance.Renamed += (s, e) => OnChanged(s, e);
					watcherInstance.Start();
					_linuxRepositoryWatchers.Add(watcherInstance);
				}
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Could not watch the repository metadata of {Path}", GitDirectory);
			}
		}

		// Several watchers fire for one Git command; coalesce them into one reload
		private void ScheduleLinuxGitRefresh()
		{
			var cts = new CancellationTokenSource();
			Interlocked.Exchange(ref _linuxGitDebounce, cts)?.Cancel();

			_ = Task.Delay(TimeSpan.FromMilliseconds(400), cts.Token).ContinueWith(
				_ => dispatcherQueue.EnqueueOrInvokeAsync(async () =>
				{
					await ReloadLinuxGitPropertiesAsync();
					GitDirectoryUpdated?.Invoke(null, null!);
				}),
				cts.Token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
		}

		// The Git columns are loaded once per item; after the repository changed they are stale, so reset the flags and reload
		private async Task ReloadLinuxGitPropertiesAsync()
		{
			if (isDisposed || !IsValidGitDirectory)
				return;

			var items = filesAndFolders.OfType<GitItem>().ToList();
			foreach (var item in items)
				item.ResetGitProperties();

			try
			{
				// The first screens reload now; the rest reload when scrolled into view because ItemPropertiesInitialized was reset
				foreach (var item in items.Take(300))
				{
					item.ItemPropertiesInitialized = true;
					await LoadGitPropertiesAsync(item);
				}
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
			Interlocked.Exchange(ref _linuxGitDebounce, null)?.Cancel();
			_linuxRefreshDebounce?.Cancel();
			_linuxWatcher?.Dispose();
			_linuxWatcher = null;
			Interlocked.Exchange(ref _linuxTrashUnsubscribe, null)?.Invoke();
		}
	}
}
