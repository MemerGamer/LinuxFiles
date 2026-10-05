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
		private Action? _linuxTrashUnsubscribe;

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

				filesAndFolders.AddRange(pending);

				await OrderFilesAndFoldersAsync();
				await ApplyFilesAndFoldersChangesAsync();

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
				return new ListedItem(null)
				{
					PrimaryItemAttribute = StorageItemTypes.Folder,
					ItemNameRaw = entry.Name,
					ItemDateModifiedReal = modified,
					ItemDateCreatedReal = created,
					ItemType = folderTypeTextLocalized,
					FileImage = null,
					IsHiddenItem = entry.IsHidden,
					Opacity = opacity,
					LoadFileIcon = false,
					ItemPath = entry.FullPath,
					FileSize = null,
					FileSizeBytes = 0,
				};
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

			return new ListedItem(null)
			{
				PrimaryItemAttribute = StorageItemTypes.File,
				FileExtension = extension,
				IsHiddenItem = entry.IsHidden,
				Opacity = opacity,
				FileImage = null,
				LoadFileIcon = false,
				ItemNameRaw = entry.Name,
				ItemDateModifiedReal = modified,
				ItemDateAccessedReal = accessed,
				ItemDateCreatedReal = created,
				ItemType = itemType,
				ItemPath = entry.FullPath,
				FileSize = entry.Length.ToSizeString(),
				FileSizeBytes = entry.Length,
			};
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

		private void CloseLinuxWatcher()
		{
			_linuxRefreshDebounce?.Cancel();
			_linuxWatcher?.Dispose();
			_linuxWatcher = null;
			Interlocked.Exchange(ref _linuxTrashUnsubscribe, null)?.Invoke();
		}
	}
}
