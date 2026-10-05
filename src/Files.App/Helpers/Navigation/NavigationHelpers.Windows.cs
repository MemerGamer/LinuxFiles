// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.System;
using WinRT;
using Windows.Win32;

namespace Files.App.Helpers
{
	public static partial class NavigationHelpers
	{
		private static readonly IWindowsRecentItemsService WindowsRecentItemsService = Ioc.Default.GetRequiredService<IWindowsRecentItemsService>();

		private static async Task<bool> OpenPathWindowsAsync(string path, IShellPage associatedInstance, FilesystemItemType? itemType, bool openSilent, bool openViaApplicationPicker, IEnumerable<string>? selectItems, string? args, bool forceOpenInNewTab)
		{
			string? previousDir = associatedInstance.ShellViewModel.WorkingDirectory;

			var fileAttributes = Win32Helper.GetFileAttributes(path);
			bool isDirectory = fileAttributes.HasFlag(System.IO.FileAttributes.Directory);

			var shortcutInfo = new ShellLinkItem();

			if (!isDirectory && FileExtensionHelpers.IsShortcutOrUrlFile(path))
			{
				var shInfo = await FileOperationsHelpers.ParseLinkAsync(path);

				if (shInfo is null)
					return false;

				itemType = shInfo.IsFolder ? FilesystemItemType.Directory : FilesystemItemType.File;

				shortcutInfo = shInfo;

				if (shortcutInfo.InvalidTarget)
				{
					if (await DialogDisplayHelper.ShowDialogAsync(DynamicDialogFactory.GetFor_ShortcutNotFound(shortcutInfo.TargetPath)) != DynamicDialogResult.Primary)
						return false;

					// Delete shortcut
					var shortcutItem = StorageHelpers.FromPathAndType(path, FilesystemItemType.File);
					await associatedInstance.FilesystemHelpers.DeleteItemAsync(shortcutItem, DeleteConfirmationPolicies.Never, false, true);
				}
			}
			else if (fileAttributes.HasFlag(System.IO.FileAttributes.ReparsePoint))
			{
				if (!isDirectory &&
					Win32Helper.GetWin32FindDataForPath(path, out var findData) &&
					findData.dwReserved0 == PInvoke.IO_REPARSE_TAG_SYMLINK)
				{
					shortcutInfo.TargetPath = Win32Helper.ParseSymLink(path);
				}
				itemType ??= isDirectory ? FilesystemItemType.Directory : FilesystemItemType.File;
			}
			else if (fileAttributes.HasFlag(System.IO.FileAttributes.Hidden))
			{
				itemType = isDirectory ? FilesystemItemType.Directory : FilesystemItemType.File;
			}
			else if (itemType is null)
			{
				itemType = await StorageHelpers.GetTypeFromPath(path);
			}

			FilesystemResult opened = (FilesystemResult)false;
			switch (itemType)
			{
				case FilesystemItemType.Library:
					opened = await OpenLibrary(path, associatedInstance, selectItems, forceOpenInNewTab);
					break;

				case FilesystemItemType.Directory:
					opened = await OpenDirectory(path, associatedInstance, selectItems, shortcutInfo, forceOpenInNewTab, fileAttributes.HasFlag(System.IO.FileAttributes.Hidden));
					break;

				case FilesystemItemType.File:
					// Starts the screensaver in full-screen mode
					if (FileExtensionHelpers.IsScreenSaverFile(path))
						args += "/s";

					opened = await OpenFile(path, associatedInstance, shortcutInfo, openViaApplicationPicker, args);
					break;
			}

			if (opened.ErrorCode == FileSystemStatusCode.NotFound && !openSilent)
			{
				await DialogDisplayHelper.ShowDialogAsync(Strings.FileNotFoundDialogTitle.GetLocalizedResource(), Strings.FileNotFoundDialogText.GetLocalizedResource());
				associatedInstance.ToolbarViewModel.CanRefresh = false;
				associatedInstance.ShellViewModel?.RefreshItems(previousDir);
			}

			return opened;
		}

		private static async Task<FilesystemResult> OpenLibrary(string path, IShellPage associatedInstance, IEnumerable<string>? selectItems, bool forceOpenInNewTab)
		{
			IUserSettingsService UserSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();

			var opened = (FilesystemResult)false;
			bool isHiddenItem = Win32Helper.HasFileAttribute(path, System.IO.FileAttributes.Hidden);
			if (isHiddenItem)
			{
				await OpenPath(forceOpenInNewTab, UserSettingsService.FoldersSettingsService.OpenFoldersInNewTab, path, associatedInstance);
				opened = (FilesystemResult)true;
			}
			else if (App.LibraryManager.TryGetLibrary(path, out var library))
			{
				opened = (FilesystemResult)await library.CheckDefaultSaveFolderAccess();
				if (opened)
					await OpenPathAsync(forceOpenInNewTab, UserSettingsService.FoldersSettingsService.OpenFoldersInNewTab, path, library.Text, associatedInstance, selectItems);
			}
			return opened;
		}

		private static async Task<FilesystemResult> OpenDirectory(string path, IShellPage associatedInstance, IEnumerable<string>? selectItems, ShellLinkItem shortcutInfo, bool forceOpenInNewTab, bool isHiddenItem)
		{
			IUserSettingsService UserSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();

			var opened = (FilesystemResult)false;
			bool isShortcut = FileExtensionHelpers.IsShortcutOrUrlFile(path);

			if (isShortcut)
			{
				if (string.IsNullOrEmpty(shortcutInfo.TargetPath))
				{
					await Win32Helper.InvokeWin32ComponentAsync(path, associatedInstance);
					opened = (FilesystemResult)true;
				}
				else
				{
					await OpenPath(forceOpenInNewTab, UserSettingsService.FoldersSettingsService.OpenFoldersInNewTab, shortcutInfo.TargetPath, associatedInstance, selectItems);
					opened = (FilesystemResult)true;
				}
			}
			else if (isHiddenItem)
			{
				await OpenPath(forceOpenInNewTab, UserSettingsService.FoldersSettingsService.OpenFoldersInNewTab, path, associatedInstance);
				opened = (FilesystemResult)true;
			}
			else
			{
				if (associatedInstance.ShellViewModel is not null)
				{
					opened = await associatedInstance.ShellViewModel.GetFolderWithPathFromPathAsync(path)
						.OnSuccess(async (childFolder) =>
						{
							var folder = childFolder!;
							// Add location to Recent Items List.
							// File.Exists distinguishes an archive root (real file on disk) from an inner path like "archive.zip\sub".
							await STATask.RunPooled(() =>
							{
								if (folder.Item is SystemStorageFolder ||
									(folder.Item is ZipStorageFolder && File.Exists(folder.Path)))
									return WindowsRecentItemsService.Add(folder.Path);

								return false;
							}, App.Logger);
						});
				}
				if (!opened)
					opened = (FilesystemResult)await Task.Run(() => FolderHelpers.CheckFolderAccessWithWin32(path));

				if (opened)
					await OpenPath(forceOpenInNewTab, UserSettingsService.FoldersSettingsService.OpenFoldersInNewTab, path, associatedInstance, selectItems);
				else
					await Win32Helper.InvokeWin32ComponentAsync(path, associatedInstance);
			}
			return opened;
		}

		private static async Task<FilesystemResult> OpenFile(string path, IShellPage associatedInstance, ShellLinkItem shortcutInfo, bool openViaApplicationPicker = false, string? args = default)
		{
			var opened = (FilesystemResult)false;
			bool isHiddenItem = Win32Helper.HasFileAttribute(path, System.IO.FileAttributes.Hidden);
			bool isShortcut = FileExtensionHelpers.IsShortcutOrUrlFile(path) || !string.IsNullOrEmpty(shortcutInfo.TargetPath);

			if (isShortcut)
			{
				// Empty or non-rooted shell target (e.g. a shell:appsfolder app): launch the .lnk so the shell activates it
				if (string.IsNullOrEmpty(shortcutInfo.TargetPath) || !Path.IsPathRooted(shortcutInfo.TargetPath))
				{
					await Win32Helper.InvokeWin32ComponentAsync(path, associatedInstance, args);
				}
				else
				{
					if (!FileExtensionHelpers.IsWebLinkFile(path) && associatedInstance.ShellViewModel is not null)
					{
						var childFileResult = await associatedInstance.ShellViewModel.GetFileWithPathFromPathAsync(shortcutInfo.TargetPath);
						// Add location to Recent Items List
						if (childFileResult.Result is { Item: SystemStorageFile } childFile)
							WindowsRecentItemsService.Add(childFile.Path);
					}
					await Win32Helper.InvokeWin32ComponentAsync(shortcutInfo.TargetPath, associatedInstance, $"{args} {shortcutInfo.Arguments}", shortcutInfo.RunAsAdmin, shortcutInfo.WorkingDirectory);
				}
				opened = (FilesystemResult)true;
			}
			else if (isHiddenItem)
			{
				await Win32Helper.InvokeWin32ComponentAsync(path, associatedInstance, args);
			}
			else
			{
				if (associatedInstance.ShellViewModel is not null)
				{
					var shellViewModel = associatedInstance.ShellViewModel;

					opened = await shellViewModel.GetFileWithPathFromPathAsync(path)
						.OnSuccess(async childFile =>
						{
							var file = childFile!;
							// Add location to Recent Items List
							if (file.Item is SystemStorageFile)
								WindowsRecentItemsService.Add(file.Path);

							if (openViaApplicationPicker)
							{
								var storageFile = file.Item
									?? throw new InvalidOperationException("The file cannot be opened with the application picker because it has no storage item.");

								LauncherOptions options = InitializeWithWindow(new LauncherOptions
								{
									DisplayApplicationPicker = true
								});
								if (!await Launcher.LaunchFileAsync(storageFile, options))
									await ContextMenu.InvokeVerb("openas", path);
							}
							else
							{
								var fileExtension = Path.GetExtension(path);

								// Use NeighboringFilesQuery to launch photos
								// The query options no longer work with the Windows 11 Photo App but they still work for Windows 10
								if (FileExtensionHelpers.IsImageFile(fileExtension))
								{
									//try using launcher first
									bool launchSuccess = false;

									// The Windows 11 Photos app ignores NeighboringFilesQuery when launched as default app.
									// Use the app URI only when this extension is associated with Microsoft Photos.
									if (FileAssociationHelpers.IsMicrosoftPhotosDefaultAssociation(fileExtension))
									{
										string uri = $"ms-photos:viewer?fileName={Uri.EscapeDataString(path)}";
										launchSuccess = await Launcher.LaunchUriAsync(new Uri(uri));
									}

									BaseStorageFileQueryResult? fileQueryResult = null;
									//Get folder to create a file query (to pass to apps like Photos, Movies & TV..., needed to scroll through the folder like what Windows Explorer does)
									var currentFolderResult = await shellViewModel.GetFolderFromPathAsync(PathNormalization.GetParentDir(path));
									if (!launchSuccess && currentFolderResult.Result is { } currentFolder)
									{
										QueryOptions queryOptions = new(CommonFileQuery.DefaultQuery, null);
										//We can have many sort entries
										SortEntry sortEntry = new()
										{
											AscendingOrder = associatedInstance.InstanceViewModel.FolderSettings.DirectorySortDirection == SortDirection.Ascending
										};
										//Basically we tell to the launched app to follow how we sorted the files in the directory.
										var sortOption = associatedInstance.InstanceViewModel.FolderSettings.DirectorySortOption;
										switch (sortOption)
										{
											case SortOption.Name:
												sortEntry.PropertyName = "System.ItemNameDisplay";
												queryOptions.SortOrder.Clear();
												queryOptions.SortOrder.Add(sortEntry);
												break;
											case SortOption.DateModified:
												sortEntry.PropertyName = "System.DateModified";
												queryOptions.SortOrder.Clear();
												queryOptions.SortOrder.Add(sortEntry);
												break;
											case SortOption.DateCreated:
												sortEntry.PropertyName = "System.DateCreated";
												queryOptions.SortOrder.Clear();
												queryOptions.SortOrder.Add(sortEntry);
												break;
											//Unfortunately this is unsupported | Remarks: https://learn.microsoft.com/uwp/api/windows.storage.search.queryoptions.sortorder?view=winrt-19041
											//case Enums.SortOption.Size:
											//sortEntry.PropertyName = "System.TotalFileSize";
											//queryOptions.SortOrder.Clear();
											//queryOptions.SortOrder.Add(sortEntry);
											//break;
											//Unfortunately this is unsupported | Remarks: https://learn.microsoft.com/uwp/api/windows.storage.search.queryoptions.sortorder?view=winrt-19041
											//case Enums.SortOption.FileType:
											//sortEntry.PropertyName = "System.FileExtension";
											//queryOptions.SortOrder.Clear();
											//queryOptions.SortOrder.Add(sortEntry);
											//break;
											//Handle unsupported
											default:
												//keep the default one in SortOrder IList
												break;
										}
										var options = InitializeWithWindow(new LauncherOptions());
										if (currentFolder.AreQueryOptionsSupported(queryOptions))
										{
											fileQueryResult = currentFolder.CreateFileQueryWithOptions(queryOptions);
											options.NeighboringFilesQuery = fileQueryResult.ToStorageFileQueryResult();
										}
										// Now launch file with options.
										if (file.Item is { } item &&
											(await FilesystemTasks.Wrap(() => item.ToStorageFileAsync().AsTask())).Result is { } storageItem)
										{
											launchSuccess = await Launcher.LaunchFileAsync(storageItem, options);
										}
									}
									if (!launchSuccess)
										await Win32Helper.InvokeWin32ComponentAsync(path, associatedInstance, args);
								}
								else if (file.Item is ZipStorageFile zipStorageFile)
								{
									var options = InitializeWithWindow(new LauncherOptions());
									var storageItem = (await FilesystemTasks.Wrap(() => zipStorageFile.ToStorageFileAsync().AsTask())).Result;
									if (storageItem is null)
									{
										await Win32Helper.InvokeWin32ComponentAsync(path, associatedInstance, args);
									}
									else if (!await Launcher.LaunchFileAsync(storageItem, options))
									{
										var pickerOptions = InitializeWithWindow(new LauncherOptions
										{
											DisplayApplicationPicker = true
										});
										if (!await Launcher.LaunchFileAsync(storageItem, pickerOptions))
											await Win32Helper.InvokeWin32ComponentAsync(path, associatedInstance, args);
									}
								}
								else
								{
									await Win32Helper.InvokeWin32ComponentAsync(path, associatedInstance, args);
								}
							}
						});
				}
			}
			return opened;
		}

		private static LauncherOptions InitializeWithWindow(LauncherOptions obj)
		{
			WinRT.Interop.InitializeWithWindow.Initialize(obj, MainWindow.Instance.WindowHandle);
			return obj;
		}
	}
}
