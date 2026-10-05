// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;
using Windows.Win32;
using Windows.Win32.System.SystemServices;
using static Files.App.Helpers.Win32PInvoke;
using ByteSize = ByteSizeLib.ByteSize;
using FileAttributes = System.IO.FileAttributes;

namespace Files.App.ViewModels
{
	/// <summary>
	/// Windows folder listing (FindFirstFileEx, StorageFolder queries), ReadDirectoryChangesW watchers, alternate data streams and
	/// Recycle Bin shell items of <see cref="ShellViewModel"/>. Kept byte-identical to upstream where possible.
	/// </summary>
	public sealed partial class ShellViewModel
	{
		private async void RecycleBinRefreshRequestedAsync(object? sender, FileSystemEventArgs e)
		{
			if (!Constants.UserEnvironmentPaths.RecycleBinPath.Equals(CurrentFolder?.ItemPath, StringComparison.OrdinalIgnoreCase))
				return;

			await dispatcherQueue.EnqueueOrInvokeAsync(() =>
			{
				RefreshItems(null);
			});
		}

		private async void RecycleBinItemDeletedAsync(object? sender, FileSystemEventArgs e)
		{
			if (!Constants.UserEnvironmentPaths.RecycleBinPath.Equals(CurrentFolder?.ItemPath, StringComparison.OrdinalIgnoreCase))
				return;

			var removedItem = await RemoveFileOrFolderAsync(e.FullPath);

			if (removedItem is not null)
				await ApplyFilesAndFoldersChangesAsync();
		}

		private async void RecycleBinItemCreatedAsync(object? sender, FileSystemEventArgs e)
		{
			if (!Constants.UserEnvironmentPaths.RecycleBinPath.Equals(CurrentFolder?.ItemPath, StringComparison.OrdinalIgnoreCase))
				return;

			using var folderItem = SafetyExtensions.IgnoreExceptions(() => new ShellItem(e.FullPath));
			if (folderItem is null)
				return;

			var shellFileItem = ShellFolderExtensions.GetShellFileItem(folderItem);
			if (shellFileItem is null)
				throw new InvalidDataException("The recycle-bin item could not be converted to a shell item.");

			var newListedItem = await AddFileOrFolderFromShellFile(shellFileItem);
			if (newListedItem is null)
				return;

			await AddFileOrFolderAsync(newListedItem);
			await OrderFilesAndFoldersAsync();
			await ApplyFilesAndFoldersChangesAsync();
		}

		partial void ApplyDriveStorageDetails(ListedItem item, IDictionary<string, object> properties)
		{
				// Drive Storage Details
				if (properties["System.SFGAOFlags"] is uint attributesRaw &&
					properties["System.Capacity"] is ulong capacityRaw &&
					properties["System.FreeSpace"] is ulong freeSpaceRaw &&
					((SFGAO_FLAGS)attributesRaw).HasFlag(SFGAO_FLAGS.SFGAO_REMOVABLE) &&
					!((SFGAO_FLAGS)attributesRaw).HasFlag(SFGAO_FLAGS.SFGAO_FILESYSTEM))
				{
					var maxSpace = ByteSize.FromBytes(capacityRaw);
					var freeSpace = ByteSize.FromBytes(freeSpaceRaw);

					item.MaxSpace = maxSpace;
					item.SpaceUsed = maxSpace - freeSpace;
					item.FileSize = string.Format(Strings.DriveFreeSpaceAndCapacity.GetLocalizedResource(), freeSpace.ToSizeString(), maxSpace.ToSizeString());
					item.ShowDriveStorageDetails = true;
				}
		}

		private async Task PromptToUnlockBitlockerIfLockedAsync(string path, string pathRoot)
		{
			try
			{
				var rootFolder = await FilesystemTasks.WrapNullable(() => StorageFileExtensions.DangerousGetFolderFromPathAsync(path));
				if (await FolderHelpers.CheckBitlockerStatusAsync(rootFolder, path))
					await ContextMenu.InvokeVerb("unlock-bde", pathRoot);
			}
			catch (Exception ex)
			{
				// Runs detached, so swallow: the property probe or the unlock-bde shell verb can throw (e.g. COMException)
				App.Logger.LogWarning(ex, ex.Message);
			}
		}

		private async Task<int> EnumerateWindowsFolderAsync(string path, CancellationToken cancellationToken, LibraryItem? library)
		{
			// Flag to use FindFirstFileExFromApp or StorageFolder enumeration - Use storage folder for Box Drive (#4629)
			var isBoxFolder = CloudDrivesManager.Drives.FirstOrDefault(x => x.Text == "Box")?.Path?.TrimEnd('\\') is string boxFolder && path.StartsWith(boxFolder);
			bool isWslDistro = path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase)
				|| path.Equals(@"\\wsl$", StringComparison.OrdinalIgnoreCase) || path.Equals(@"\\wsl.localhost", StringComparison.OrdinalIgnoreCase);
			bool isMtp = path.StartsWith(@"\\?\", StringComparison.Ordinal);
			bool isShellFolder = path.StartsWith(@"\\SHELL\", StringComparison.Ordinal);
			bool isNetwork = path.StartsWith(@"\\", StringComparison.Ordinal) &&
				!isMtp &&
				!isShellFolder &&
				!isWslDistro;
			bool isNetdisk = false;

			try
			{
				// Special handling for network drives
				if (!isNetwork)
					isNetdisk = await Task.Run(() => new DriveInfo(path).DriveType == System.IO.DriveType.Network);
			}
			catch { }

			bool isFtp = FtpHelpers.IsFtpPath(path);
			bool enumFromStorageFolder = isBoxFolder || isFtp;

			BaseStorageFolder? rootFolder = null;

			if (isNetwork || isNetdisk)
			{
				var auth = await NetworkService.AuthenticateNetworkShare(path, cancellationToken);
				if (!auth)
					return -1;
			}

			// Off the UI thread: FindFirstFileEx blocks until the SMB timeout on an unreachable share.
			Win32PInvoke.SafeFindHandle? hFile = null;
			WIN32_FIND_DATA findData = default;
			int errorCode = 0;
			if (!enumFromStorageFolder)
			{
				(hFile, findData, errorCode) = await Task.Run(() =>
				{
					var hFileTsk = FindFirstFileExFromAppSafe(
						path + "\\*.*",
						FINDEX_INFO_LEVELS.FindExInfoBasic,
						out WIN32_FIND_DATA findDataTsk,
						FINDEX_SEARCH_OPS.FindExSearchNameMatch,
						IntPtr.Zero,
						FIND_FIRST_EX_LARGE_FETCH);

					return (hFileTsk, findDataTsk, hFileTsk.IsInvalid ? Marshal.GetLastWin32Error() : 0);
				})
				.WithTimeoutAsync(TimeSpan.FromSeconds(5));
			}

			if (!enumFromStorageFolder && hFile is not null && !hFile.IsInvalid)
			{
				// Enumerate with the handle opened above; rootFolder only used for Bitlocker
				currentStorageFolder = null;
			}
			else if (workingRoot is not null)
			{
				var res = await FilesystemTasks.Wrap(() => StorageFileExtensions.DangerousGetFolderWithPathFromPathAsync(path, workingRoot, currentStorageFolder));
				if (!res)
					return -1;

				var storageFolder = res.Result
					?? throw new InvalidOperationException("A successful folder lookup did not return a storage folder.");

				currentStorageFolder = storageFolder;
				rootFolder = storageFolder.Item;
				enumFromStorageFolder = true;
			}
			else
			{
				var res = await FilesystemTasks.Wrap(() => StorageFileExtensions.DangerousGetFolderWithPathFromPathAsync(path, workingRoot, currentStorageFolder));
				if (res)
				{
					var storageFolder = res.Result
						?? throw new InvalidOperationException("A successful folder lookup did not return a storage folder.");
					currentStorageFolder = storageFolder;
					rootFolder = storageFolder.Item;
				}
				else if (res == FileSystemStatusCode.Unauthorized)
				{
					ShowLocationInaccessibleOrMissing(path);

					return -1;
				}
				else if (res == FileSystemStatusCode.NotFound)
				{
					ShowLocationInaccessibleOrMissing(path);

					return -1;
				}
				else
				{
					ShowLocationUnavailable(LocationUnavailableKind.DriveUnplugged, res.ErrorCode.ToString());

					return -1;
				}
			}

			var pathRoot = Path.GetPathRoot(path);
			if (Path.IsPathRooted(path) && pathRoot == path)
			{
				// Off the critical path: a locked drive fails enumeration anyway, so don't block the listing on the BitLocker probe.
				_ = PromptToUnlockBitlockerIfLockedAsync(path, pathRoot);
			}

			HasNoWatcher = isFtp || isWslDistro || isMtp || currentStorageFolder?.Item is ZipStorageFolder;

			if (enumFromStorageFolder)
			{
				// The handle from the open above is unused on the storage-folder path.
				hFile?.Dispose();

				var basicProps = await rootFolder?.GetBasicPropertiesAsync();
				var currentFolder = library ?? new ListedItem(rootFolder?.FolderRelativeId ?? string.Empty)
				{
					PrimaryItemAttribute = StorageItemTypes.Folder,
					ItemPropertiesInitialized = true,
					ItemNameRaw = rootFolder?.DisplayName ?? string.Empty,
					ItemDateModifiedReal = basicProps.DateModified,
					ItemType = rootFolder?.DisplayType ?? string.Empty,
					FileImage = null,
					LoadFileIcon = false,
					ItemPath = string.IsNullOrEmpty(rootFolder?.Path) ? currentStorageFolder?.Path ?? string.Empty : rootFolder.Path,
					FileSize = null,
					FileSizeBytes = 0,
				};

				if (library is null)
					currentFolder.ItemDateCreatedReal = rootFolder?.DateCreated ?? DateTimeOffset.Now;

				CurrentFolder = currentFolder;
				await EnumFromStorageFolderAsync(path, rootFolder, currentStorageFolder, cancellationToken);

				// Workaround for #7428
				return isBoxFolder ? 2 : 1;
			}
			else
			{
				var itemModifiedDate = DateTime.Now;
				var itemCreatedDate = DateTime.Now;

				try
				{
					FileTimeToSystemTime(in findData.ftLastWriteTime, out var systemModifiedTimeOutput);
					itemModifiedDate = systemModifiedTimeOutput.ToDateTime();

					FileTimeToSystemTime(in findData.ftCreationTime, out SYSTEMTIME systemCreatedTimeOutput);
					itemCreatedDate = systemCreatedTimeOutput.ToDateTime();
				}
				catch (ArgumentException)
				{
				}

				var isHidden = (((FileAttributes)findData.dwFileAttributes & FileAttributes.Hidden) == FileAttributes.Hidden);
				var opacity = isHidden ? Constants.UI.DimItemOpacity : 1d;

				var currentFolder = library ?? new ListedItem(null)
				{
					PrimaryItemAttribute = StorageItemTypes.Folder,
					ItemPropertiesInitialized = true,
					ItemNameRaw = rootFolder?.DisplayName ?? Path.GetFileName(path.TrimEnd('\\')),
					ItemDateModifiedReal = itemModifiedDate,
					ItemDateCreatedReal = itemCreatedDate,
					ItemType = folderTypeTextLocalized,
					FileImage = null,
					IsHiddenItem = isHidden,
					Opacity = opacity,
					LoadFileIcon = false,
					ItemPath = path,
					FileSize = null,
					FileSizeBytes = 0,
				};

				CurrentFolder = currentFolder;

				if (hFile is null)
				{
					ShowLocationUnavailable(LocationUnavailableKind.DriveUnplugged);

					return -1;
				}
				else if (hFile.IsInvalid)
				{
					hFile.Dispose();
					await EnumFromStorageFolderAsync(path, rootFolder, currentStorageFolder, cancellationToken);

					// errorCode == ERROR_ACCESS_DENIED
					if (filesAndFolders.Count == 0 && errorCode == 0x5)
					{
						ShowLocationInaccessibleOrMissing(path);

						return -1;
					}

					return 1;
				}
				else
				{
					await Task.Run(async () =>
					{
						List<ListedItem> fileList = await Win32StorageEnumerator.ListEntries(path, hFile, findData, cancellationToken, -1, GetPreloadIconSize(), intermediateAction: async (intermediateList) =>
						{
							filesAndFolders.AddRange(intermediateList);

							// The stable sort lands new items in final position without reordering visible ones; capped for huge listings
							if (filesAndFolders.Count <= 10_000)
								await OrderFilesAndFoldersAsync();

							await ApplyFilesAndFoldersChangesAsync();
						});

						filesAndFolders.AddRange(fileList);

						await OrderFilesAndFoldersAsync();
						await ApplyFilesAndFoldersChangesAsync();
						// Not awaited here: with Low priority these don't run until the UI thread goes idle
						// after the final list update, which would delay load completion and watcher setup.
						// The desktop.ini task is awaited before applying the adaptive layout, which reads DesktopIni.
						_ = dispatcherQueue.EnqueueOrInvokeAsync(CheckForSolutionFile, Microsoft.UI.Dispatching.DispatcherQueuePriority.Low);
						desktopIniUpdateTask = dispatcherQueue.EnqueueOrInvokeAsync(() =>
						{
							GetDesktopIniFileData();
							CheckForBackgroundImage();
						},
						Microsoft.UI.Dispatching.DispatcherQueuePriority.Low);
					});

					// Cache the resolved folder so the post-enum switch reuses it.
					currentStorageFolder ??= await FilesystemTasks.Wrap(() => StorageFileExtensions.DangerousGetFolderWithPathFromPathAsync(path));
					rootFolder ??= currentStorageFolder?.Item;
					if (rootFolder is not null)
					{
						if (rootFolder.DisplayName is not null)
							currentFolder.ItemNameRaw = rootFolder.DisplayName;

						if (!string.Equals(path, Constants.UserEnvironmentPaths.RecycleBinPath, StringComparison.OrdinalIgnoreCase))
						{
							enumeratedCloudSyncStatus = await CheckCloudDriveSyncStatusAsync(rootFolder);
							currentFolder.SyncStatusUI = CloudDriveSyncStatusUI.FromCloudDriveSyncStatus(enumeratedCloudSyncStatus.Value);
						}
					}

					return 0;
				}
			}
		}

		private async Task EnumFromStorageFolderAsync(string path, BaseStorageFolder? rootFolder, StorageFolderWithPath? currentStorageFolder, CancellationToken cancellationToken)
		{
			if (rootFolder is null)
				return;

			// Null when a concurrent navigation or dispose cleared the context; this enumeration is stale
			if (currentStorageFolder is null)
				return;

			if (rootFolder is IPasswordProtectedItem ppis)
				ppis.PasswordRequestedCallback = async (item) =>
				{
					await dispatcherQueue.EnqueueOrInvokeAsync(() => ShowLocationUnavailable(LocationUnavailableKind.PasswordRequired));

					return await UIFilesystemHelpers.RequestPassword(item);
				};

			try
			{
				await Task.Run(async () =>
				{
					List<ListedItem> finalList = await UniversalStorageEnumerator.ListEntries(
						rootFolder,
						currentStorageFolder,
						cancellationToken,
						-1,
						GetPreloadIconSize(),
						async (intermediateList) =>
						{
							filesAndFolders.AddRange(intermediateList);

							// The stable sort lands new items in final position without reordering visible ones; capped for huge listings
							if (filesAndFolders.Count <= 10_000)
								await OrderFilesAndFoldersAsync();

							await ApplyFilesAndFoldersChangesAsync();
						});

					filesAndFolders.AddRange(finalList);

					await OrderFilesAndFoldersAsync();
					await ApplyFilesAndFoldersChangesAsync();
				}, cancellationToken);

				IsLocationUnavailable = false;
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) // Password dialog dismissed
			{
				ShowLocationUnavailable(LocationUnavailableKind.PasswordRequired);
			}

			if (rootFolder is IPasswordProtectedItem ppiu)
				ppiu.PasswordRequestedCallback = null;
		}

		private async Task WatchForStorageFolderChangesAsync(BaseStorageFolder? rootFolder)
		{
			if (rootFolder is null)
				return;

			await Task.Run(() =>
			{
				var options = new QueryOptions()
				{
					FolderDepth = FolderDepth.Shallow,
					IndexerOption = IndexerOption.OnlyUseIndexerAndOptimizeForIndexedProperties
				};

				options.SetPropertyPrefetch(PropertyPrefetchOptions.None, null);
				options.SetThumbnailPrefetch(ThumbnailMode.ListView, 0, ThumbnailOptions.ReturnOnlyIfCached);

				if (rootFolder.AreQueryOptionsSupported(options))
				{
					var itemQueryResult = rootFolder.CreateItemQueryWithOptions(options).ToStorageItemQueryResult()
						?? throw new InvalidOperationException("The folder query could not be converted to a storage query.");

					itemQueryResult.ContentsChanged += ItemQueryResult_ContentsChanged;

					// Just get one item to start getting notifications
					var watchedItemsOperation = itemQueryResult.GetItemsAsync(0, 1);

					watcherCTS.Token.Register(() =>
					{
						itemQueryResult.ContentsChanged -= ItemQueryResult_ContentsChanged;
						watchedItemsOperation?.Cancel();
					});
				}
			});
		}

		private void WatchForWin32FolderChanges(string? folderPath)
		{
			if (!Directory.Exists(folderPath))
				return;

			// NOTE: Suppressed NullReferenceException caused by EnableRaisingEvents
			SafetyExtensions.IgnoreExceptions(() =>
			{
				watcher = new FileSystemWatcher
				{
					Path = folderPath,
					Filter = "*.*",
					NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
				};

				watcher.Created += DirectoryWatcher_Changed;
				watcher.Deleted += DirectoryWatcher_Changed;
				watcher.Renamed += DirectoryWatcher_Changed;
				watcher.EnableRaisingEvents = true;
			}, App.Logger);
		}

		private async void DirectoryWatcher_Changed(object sender, FileSystemEventArgs e)
		{
			Debug.WriteLine($"Directory watcher event: {e.ChangeType}, {e.FullPath}");

			await dispatcherQueue.EnqueueOrInvokeAsync(() =>
			{
				RefreshItems(null);
			});
		}

		private async void ItemQueryResult_ContentsChanged(IStorageQueryResultBase sender, object args)
		{
			// Query options have to be reapplied otherwise old results are returned
			var options = new QueryOptions()
			{
				FolderDepth = FolderDepth.Shallow,
				IndexerOption = IndexerOption.OnlyUseIndexerAndOptimizeForIndexedProperties
			};

			options.SetPropertyPrefetch(PropertyPrefetchOptions.None, null);
			options.SetThumbnailPrefetch(ThumbnailMode.ListView, 0, ThumbnailOptions.ReturnOnlyIfCached);

			sender.ApplyNewQueryOptions(options);

			await dispatcherQueue.EnqueueOrInvokeAsync(() =>
			{
				RefreshItems(null);
			});
		}

		private void WatchForDirectoryChanges(string path, CloudDriveSyncStatus syncStatus)
		{
			// Enumeration is fire-and-forget; don't set up a watcher on a disposed view model.
			if (isDisposed)
				return;

			Debug.WriteLine($"WatchForDirectoryChanges: {path}");
			var hWatchDir = Win32PInvoke.CreateFileFromApp(path, 1, 1 | 2 | 4,
				IntPtr.Zero, 3, (uint)Win32PInvoke.File_Attributes.BackupSemantics | (uint)Win32PInvoke.File_Attributes.Overlapped, IntPtr.Zero);
			if (hWatchDir.ToInt64() == -1)
				return;

			var hasSyncStatus = syncStatus != CloudDriveSyncStatus.NotSynced && syncStatus != CloudDriveSyncStatus.Unknown;

			aProcessQueueAction ??= Task.Run(() => ProcessOperationQueueAsync(watcherCTS.Token, hasSyncStatus));

			var aWatcherAction = Windows.System.Threading.ThreadPool.RunAsync((x) =>
			{
				var buff = new byte[4096];
				var rand = Guid.NewGuid();
				var notifyFilters = FILE_NOTIFY_CHANGE_DIR_NAME | FILE_NOTIFY_CHANGE_FILE_NAME | FILE_NOTIFY_CHANGE_LAST_WRITE | FILE_NOTIFY_CHANGE_SIZE;

				if (hasSyncStatus)
					notifyFilters |= FILE_NOTIFY_CHANGE_ATTRIBUTES;

				var overlapped = new NativeOverlapped();
				using var eventHandle = PInvoke.CreateEvent(null, false, false, null);
				overlapped.EventHandle = eventHandle.DangerousGetHandle();
				const uint INFINITE = 0xFFFFFFFF;

				while (x.Status != AsyncStatus.Canceled)
				{
					unsafe
					{
						fixed (byte* pBuff = buff)
						{
							ref var notifyInformation = ref Unsafe.As<byte, FILE_NOTIFY_INFORMATION>(ref buff[0]);
							if (x.Status != AsyncStatus.Canceled)
							{
								PInvoke.ReadDirectoryChanges(
									new Windows.Win32.Foundation.HANDLE(hWatchDir),
									pBuff,
									4096,
									false,
									(Windows.Win32.Storage.FileSystem.FILE_NOTIFY_CHANGE)notifyFilters,
									null,
									&overlapped,
									null);
							}
							else
							{
								break;
							}

							Debug.WriteLine("waiting: {0}", rand);
							if (x.Status == AsyncStatus.Canceled)
								break;

							var rc = WaitForSingleObjectEx(overlapped.EventHandle, INFINITE, true);
							Debug.WriteLine("wait done: {0}", rand);

							uint offset = 0;
							ref var notifyInfo = ref Unsafe.As<byte, FILE_NOTIFY_INFORMATION>(ref buff[offset]);
							if (x.Status == AsyncStatus.Canceled)
								break;

							do
							{
								notifyInfo = ref Unsafe.As<byte, FILE_NOTIFY_INFORMATION>(ref buff[offset]);
								string? FileName = null;
								unsafe
								{
									fixed (char* name = notifyInfo.FileName)
									{
										FileName = Path.Combine(path, new string(name, 0, (int)notifyInfo.FileNameLength / 2));
									}
								}

								uint action = notifyInfo.Action;

								Debug.WriteLine("action: {0}", action);

								operationQueue.Enqueue((action, FileName));

								offset += notifyInfo.NextEntryOffset;
							}
							while (notifyInfo.NextEntryOffset != 0 && x.Status != AsyncStatus.Canceled);

							operationEvent.Set();

							//ResetEvent(overlapped.hEvent);
							Debug.WriteLine("Task running...");
						}
					}
				}

				operationQueue.Clear();

				Debug.WriteLine("aWatcherAction done: {0}", rand);
			});

			watcherCTS.Token.Register(() =>
			{
				if (aWatcherAction is not null)
				{
					aWatcherAction?.Cancel();

					// Prevent duplicate execution of this block
					aWatcherAction = null;

					Debug.WriteLine("watcher canceled");
				}

				CancelIoEx(hWatchDir, IntPtr.Zero);
				CloseHandle(hWatchDir);
			});
		}

		private void WatchForGitChanges()
		{
			// Enumeration is fire-and-forget; don't set up a watcher on a disposed view model.
			if (isDisposed)
				return;

			var hWatchDir = Win32PInvoke.CreateFileFromApp(
				GitDirectory!,
				1,
				1 | 2 | 4,
				IntPtr.Zero,
				3,
				(uint)Win32PInvoke.File_Attributes.BackupSemantics | (uint)Win32PInvoke.File_Attributes.Overlapped,
				IntPtr.Zero);

			if (hWatchDir.ToInt64() == -1)
				return;

			gitProcessQueueAction ??= Task.Run(() => ProcessGitChangesQueueAsync(watcherCTS.Token));

			var gitWatcherAction = Windows.System.Threading.ThreadPool.RunAsync((x) =>
			{
				var buff = new byte[4096];
				var rand = Guid.NewGuid();
				var notifyFilters = FILE_NOTIFY_CHANGE_DIR_NAME | FILE_NOTIFY_CHANGE_FILE_NAME | FILE_NOTIFY_CHANGE_LAST_WRITE | FILE_NOTIFY_CHANGE_SIZE | FILE_NOTIFY_CHANGE_CREATION;

				var overlapped = new NativeOverlapped();
				using var eventHandle = PInvoke.CreateEvent(null, false, false, null);
				overlapped.EventHandle = eventHandle.DangerousGetHandle();
				const uint INFINITE = 0xFFFFFFFF;

				while (x.Status != AsyncStatus.Canceled)
				{
					unsafe
					{
						fixed (byte* pBuff = buff)
						{
							ref var notifyInformation = ref Unsafe.As<byte, FILE_NOTIFY_INFORMATION>(ref buff[0]);
							if (x.Status == AsyncStatus.Canceled)
								break;

							PInvoke.ReadDirectoryChanges(
								new Windows.Win32.Foundation.HANDLE(hWatchDir),
								pBuff,
								4096,
								true,
								(Windows.Win32.Storage.FileSystem.FILE_NOTIFY_CHANGE)notifyFilters,
								null,
								&overlapped,
								null);

							if (x.Status == AsyncStatus.Canceled)
								break;

							var rc = WaitForSingleObjectEx(overlapped.EventHandle, INFINITE, true);

							uint offset = 0;
							ref var notifyInfo = ref Unsafe.As<byte, FILE_NOTIFY_INFORMATION>(ref buff[offset]);
							if (x.Status == AsyncStatus.Canceled)
								break;

							do
							{
								notifyInfo = ref Unsafe.As<byte, FILE_NOTIFY_INFORMATION>(ref buff[offset]);

								uint action = notifyInfo.Action;

								gitChangesQueue.Enqueue(action);

								offset += notifyInfo.NextEntryOffset;
							}
							while (notifyInfo.NextEntryOffset != 0 && x.Status != AsyncStatus.Canceled);

							gitChangedEvent.Set();
						}
					}
				}

				gitChangesQueue.Clear();
			});

			watcherCTS.Token.Register(() =>
			{
				if (gitWatcherAction is not null)
				{
					gitWatcherAction?.Cancel();

					// Prevent duplicate execution of this block
					gitWatcherAction = null;
				}

				CancelIoEx(hWatchDir, IntPtr.Zero);
				CloseHandle(hWatchDir);
			});
		}

		private async Task ProcessGitChangesQueueAsync(CancellationToken cancellationToken)
		{
			const int DELAY = 200;
			var sampler = new IntervalSampler(100);
			int changes = 0;

			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					if (await gitChangedEvent.WaitAsync(DELAY, cancellationToken))
					{
						gitChangedEvent.Reset();
						while (gitChangesQueue.TryDequeue(out var _))
							++changes;

						if (changes != 0 && sampler.CheckNow())
						{
							await dispatcherQueue.EnqueueOrInvokeAsync(() => GitDirectoryUpdated?.Invoke(null, null!));
							changes = 0;
						}
					}
				}
			}
			catch { }
		}

		private async Task ProcessOperationQueueAsync(CancellationToken cancellationToken, bool hasSyncStatus)
		{
			const uint FILE_ACTION_ADDED = 0x00000001;
			const uint FILE_ACTION_REMOVED = 0x00000002;
			const uint FILE_ACTION_MODIFIED = 0x00000003;
			const uint FILE_ACTION_RENAMED_OLD_NAME = 0x00000004;
			const uint FILE_ACTION_RENAMED_NEW_NAME = 0x00000005;

			const int UPDATE_BATCH_SIZE = 32;
			var sampler = new IntervalSampler(200);
			var updateQueue = new Queue<string>();

			var anyEdits = false;
			ListedItem? lastItemAdded = null;
			var rand = Guid.NewGuid();

			// Call when any edits have occurred
			async Task HandleChangesOccurredAsync()
			{
				await OrderFilesAndFoldersAsync();
				await ApplyFilesAndFoldersChangesAsync();

				if (lastItemAdded is not null)
				{
					await RequestSelectionAsync([lastItemAdded]);
					lastItemAdded = null;
				}

				anyEdits = false;
			}

			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					if (await operationEvent.WaitAsync(200, cancellationToken))
					{
						operationEvent.Reset();

						while (operationQueue.TryDequeue(out var operation))
						{
							if (cancellationToken.IsCancellationRequested)
								break;

							try
							{
								switch (operation.Action)
								{
									case FILE_ACTION_ADDED:
									case FILE_ACTION_RENAMED_NEW_NAME:
										lastItemAdded = await AddFileOrFolderAsync(operation.FileName);
										if (lastItemAdded is not null)
											anyEdits = true;
										break;

									case FILE_ACTION_MODIFIED:
										if (!updateQueue.Contains(operation.FileName))
											updateQueue.Enqueue(operation.FileName);
										break;

									case FILE_ACTION_REMOVED:
										var itemRemoved = await RemoveFileOrFolderAsync(operation.FileName);
										if (itemRemoved is not null)
											anyEdits = true;
										break;

									case FILE_ACTION_RENAMED_OLD_NAME:
										// Pair OLD_NAME with the following NEW_NAME so the item can be updated
										// in place and stay at its current position instead of jumping to a new
										// sorted slot after a rename (issue #4214). Leaving anyEdits false skips
										// OrderFilesAndFoldersAsync; PropertyChanged keeps the visible name in sync.
										if (operationQueue.TryPeek(out var nextOp) && nextOp.Action == FILE_ACTION_RENAMED_NEW_NAME &&
											filesAndFolders.ToList().FirstOrDefault(x => x.GetRequiredPath().Equals(operation.FileName, StringComparison.OrdinalIgnoreCase)) is { } renamed)
										{
											operationQueue.TryDequeue(out _);
											var newPath = nextOp.FileName;
											await dispatcherQueue.EnqueueOrInvokeAsync(() =>
											{
												renamed.ItemPath = newPath;
												renamed.ItemNameRaw = Path.GetFileName(newPath);
												if (renamed.PrimaryItemAttribute == StorageItemTypes.File)
													renamed.FileExtension = Path.GetExtension(newPath);
											});
										}
										else
										{
											var itemRenamedOld = await RemoveFileOrFolderAsync(operation.FileName);
											if (itemRenamedOld is not null)
												anyEdits = true;
										}
										break;
								}
							}
							catch (Exception ex)
							{
								App.Logger.LogWarning(ex, ex.Message);
							}

							if (anyEdits && sampler.CheckNow())
								await HandleChangesOccurredAsync();
						}

						var itemsToUpdate = new List<string>();
						for (var i = 0; i < UPDATE_BATCH_SIZE && updateQueue.Count > 0; i++)
							itemsToUpdate.Add(updateQueue.Dequeue());

						await UpdateFilesOrFoldersAsync(itemsToUpdate, hasSyncStatus);
					}

					if (updateQueue.Count > 0)
					{
						var itemsToUpdate = new List<string>();
						for (var i = 0; i < UPDATE_BATCH_SIZE && updateQueue.Count > 0; i++)
							itemsToUpdate.Add(updateQueue.Dequeue());

						await UpdateFilesOrFoldersAsync(itemsToUpdate, hasSyncStatus);
					}

					if (anyEdits && sampler.CheckNow())
						await HandleChangesOccurredAsync();
				}
			}
			catch
			{
				// Prevent disposed cancellation token
			}

			Debug.WriteLine("aProcessQueueAction done: {0}", rand);
		}

		public Task<ListedItem?> AddFileOrFolderFromShellFile(ShellFileItem item)
		{
			return
				item.IsFolder ?
				UniversalStorageEnumerator.AddFolderAsync(ShellStorageFolder.FromShellItem(item), currentStorageFolder, addFilesCTS.Token) :
				UniversalStorageEnumerator.AddFileAsync(ShellStorageFile.FromShellItem(item), currentStorageFolder, addFilesCTS.Token);
		}

		partial void AddAlternateStreamItems(ListedItem item)
		{
		if (UserSettingsService.FoldersSettingsService.AreAlternateStreamsVisible)
		{
			// New file added, enumerate ADS
			foreach (var ads in Win32Helper.GetAlternateStreams(item.GetRequiredPath()))
			{
				var adsItem = Win32StorageEnumerator.GetAlternateStream(ads, item);
				filesAndFolders.Add(adsItem);
			}
		}
		}

		private async Task<ListedItem?> AddFileOrFolderAsync(string fileOrFolderPath)
		{
			FINDEX_INFO_LEVELS findInfoLevel = FINDEX_INFO_LEVELS.FindExInfoBasic;
			var additionalFlags = FIND_FIRST_EX_CASE_SENSITIVE;

			IntPtr hFile = FindFirstFileExFromApp(fileOrFolderPath, findInfoLevel, out WIN32_FIND_DATA findData, FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero,
												  additionalFlags);
			if (hFile.ToInt64() == -1)
			{
				// If we cannot find the file (probably since it doesn't exist anymore) simply exit without adding it
				return null;
			}

			FindClose(hFile);

			var isSystem = ((FileAttributes)findData.dwFileAttributes & FileAttributes.System) == FileAttributes.System;
			var isHidden = ((FileAttributes)findData.dwFileAttributes & FileAttributes.Hidden) == FileAttributes.Hidden;
			var startWithDot = findData.cFileName.StartsWith('.');
			if ((isHidden &&
			   (!UserSettingsService.FoldersSettingsService.ShowHiddenItems ||
			   (isSystem && !UserSettingsService.FoldersSettingsService.ShowProtectedSystemFiles))) ||
			   (startWithDot && !UserSettingsService.FoldersSettingsService.ShowDotFiles))
			{
				// Do not add to file list if hidden/system attribute is set and system/hidden file are not to be shown
				return null;
			}

			var parentPath = Directory.GetParent(fileOrFolderPath)?.FullName
				?? throw new InvalidOperationException("The added item does not have a parent directory.");

			ListedItem? listedItem;

			// FILE_ATTRIBUTE_DIRECTORY
			if ((findData.dwFileAttributes & 0x10) > 0)
				listedItem = await Win32StorageEnumerator.GetFolder(findData, parentPath, IsValidGitDirectory, addFilesCTS.Token);
			else
				listedItem = await Win32StorageEnumerator.GetFile(findData, parentPath, IsValidGitDirectory, addFilesCTS.Token);

			if (listedItem is null)
				return null;

			await AddFileOrFolderAsync(listedItem);

			return listedItem;
		}
	}
}
