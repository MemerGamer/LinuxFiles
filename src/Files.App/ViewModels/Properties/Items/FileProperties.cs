// Copyright(c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using System.IO;

namespace Files.App.ViewModels.Properties
{
	public sealed class FileProperties : BaseProperties, IFileProperties
	{
		public ListedItem Item { get; }

		public FileProperties(
			SelectedItemsPropertiesViewModel viewModel,
			CancellationTokenSource tokenSource,
			DispatcherQueue coreDispatcher,
			ListedItem item,
			IShellPage instance)
			: base(viewModel, tokenSource, coreDispatcher, instance)
		{
			Item = item;

			GetBaseProperties();

#if WINDOWS
			ViewModel.PropertyChanged += ViewModel_PropertyChanged;
#endif
		}

		public override void GetBaseProperties()
		{
			var itemPath = Item.GetRequiredPath();

			ViewModel.ItemName = Item.Name;
			ViewModel.OriginalItemName = Item.Name;
			ViewModel.ItemType = Item.ItemType;
			ViewModel.ItemLocation = (Item as RecycleBinItem)?.ItemOriginalFolder ??
				(Path.IsPathRooted(itemPath) ? Path.GetDirectoryName(itemPath) : itemPath);
			ViewModel.ItemModifiedTimestampReal = Item.ItemDateModifiedReal;
			ViewModel.ItemCreatedTimestampReal = Item.ItemDateCreatedReal;
			ViewModel.LoadCustomIcon = Item.LoadCustomIcon;
			ViewModel.CustomIconSource = Item.CustomIconSource;
			ViewModel.LoadFileIcon = Item.LoadFileIcon;
#if WINDOWS
			ViewModel.IsDownloadedFile = Win32Helper.ReadStringFromFile($"{itemPath}:Zone.Identifier") is not null;
#endif
			ViewModel.IsEditAlbumCoverVisible =
				Item.FileExtension is not ".avi" && (
				FileExtensionHelpers.IsVideoFile(Item.FileExtension) ||
				FileExtensionHelpers.IsAudioFile(Item.FileExtension));

			if (OperatingSystem.IsLinux())
			{
				SetupLinuxShortcut(itemPath);
				return;
			}

#if WINDOWS
			if (!Item.IsShortcut)
				return;

			var shortcutItem = (IShortcutItem)Item;

			var isApplication =
				FileExtensionHelpers.IsExecutableFile(shortcutItem.TargetPath) ||
				FileExtensionHelpers.IsMsiFile(shortcutItem.TargetPath);

			ViewModel.ShortcutItemType = isApplication ? Strings.Application.GetLocalizedResource() :
				Item.IsLinkItem ? Strings.PropertiesShortcutTypeLink.GetLocalizedResource() : Strings.File.GetLocalizedResource();
			ViewModel.ShortcutItemPath = shortcutItem.TargetPath;
			ViewModel.IsShortcutItemPathReadOnly = shortcutItem.IsSymLink;
			ViewModel.ShortcutItemWorkingDir = shortcutItem.WorkingDirectory;
			ViewModel.ShortcutItemWorkingDirVisibility = Item.IsLinkItem || shortcutItem.IsSymLink ? false : true;
			ViewModel.ShortcutItemArguments = shortcutItem.Arguments;
			ViewModel.ShowWindowCommand = (ShowWindowCommand)(int)shortcutItem.ShowWindowCommand;
			ViewModel.ShortcutItemArgumentsVisibility = Item.IsLinkItem || shortcutItem.IsSymLink ? false : true;
			ViewModel.ShortcutItemWindowArgsVisibility = Item.IsLinkItem || shortcutItem.IsSymLink ? false : true;

			if (isApplication)
				ViewModel.RunAsAdmin = shortcutItem.RunAsAdmin;

			ViewModel.IsSelectedItemShortcut = FileExtensionHelpers.IsShortcutFile(Item.FileExtension);

			ViewModel.ShortcutItemOpenLinkCommand = new RelayCommand(async () =>
			{
				var shortcutPath = ViewModel.ShortcutItemPath
					?? throw new InvalidOperationException("The shortcut does not have a target path.");

				if (Item.IsLinkItem)
				{
					await Win32Helper.InvokeWin32ComponentAsync(shortcutPath, AppInstance, ViewModel.ShortcutItemArguments, ViewModel.RunAsAdmin, ViewModel.ShortcutItemWorkingDir);
				}
				else
				{
					await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(
						() => NavigationHelpers.OpenPathInNewTab(Path.GetDirectoryName(shortcutPath), true));
				}
			},
			() =>
			{
				return !string.IsNullOrWhiteSpace(ViewModel.ShortcutItemPath);
			});
#endif
		}

		private void SetupLinuxShortcut(string itemPath)
		{
			if (LinuxShortcutHelper.IsSymbolicLink(itemPath, out var target))
			{
				var directory = Path.GetDirectoryName(itemPath) ?? "/";
				var resolved = string.IsNullOrEmpty(target) ? null : Path.GetFullPath(target, directory);

				ViewModel.ShortcutItemType = Strings.PropertiesShortcutTypeLink.GetLocalizedResource();
				ViewModel.ShortcutItemPath = target;
				ViewModel.IsShortcutItemPathReadOnly = true;
				ViewModel.ShortcutItemWorkingDirVisibility = false;
				ViewModel.ShortcutItemArgumentsVisibility = false;
				ViewModel.ShortcutItemWindowArgsVisibility = false;
				ViewModel.IsLinuxOpenTargetAvailable = resolved is not null;
				ViewModel.ShortcutItemOpenLinkCommand = new RelayCommand(
					async () =>
					{
						// Folder targets open themselves, anything else opens its parent folder
						var location = Directory.Exists(resolved) ? resolved : Path.GetDirectoryName(resolved);
						if (!string.IsNullOrEmpty(location))
							await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() => NavigationHelpers.OpenPathInNewTab(location, true));
					},
					() => !string.IsNullOrEmpty(resolved));
			}
			else if (LinuxShortcutHelper.IsDesktopEntry(itemPath))
			{
				var (name, exec) = LinuxShortcutHelper.ReadDesktopEntry(itemPath);
				ViewModel.ShortcutItemType = Strings.Application.GetLocalizedResource();
				ViewModel.DesktopEntryName = name;
				ViewModel.ShortcutItemPath = exec;
				ViewModel.IsShortcutItemPathReadOnly = true;
				ViewModel.ShortcutItemWorkingDirVisibility = false;
				ViewModel.ShortcutItemArgumentsVisibility = false;
				ViewModel.ShortcutItemWindowArgsVisibility = false;
			}
		}

		private async Task GetSpecialPropertiesLinuxAsync(string itemPath)
		{
			ApplyLinuxStat(itemPath);
			await ApplyLinuxTypeAsync(itemPath);

			try
			{
				var result = await FileThumbnailHelper.GetIconAsync(itemPath, Constants.ShellIconSizes.ExtraLarge, false, IconOptions.None);
				if (result is not null)
				{
					ViewModel.IconData = result;
					ViewModel.LoadUnknownTypeGlyph = false;
					ViewModel.LoadFileIcon = true;
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				App.Logger.LogWarning(ex, "Could not load the properties icon");
			}
		}

		public override async Task GetSpecialPropertiesAsync()
		{
			var itemPath = Item.GetRequiredPath();

			if (OperatingSystem.IsLinux())
			{
				await GetSpecialPropertiesLinuxAsync(itemPath);
				return;
			}

#if WINDOWS
			// Check if item is on device (not online)
			var isOnDevice = Item.SyncStatusUI.SyncStatus is not CloudDriveSyncStatus.FileOnline and not CloudDriveSyncStatus.FolderOnline;

			// Set basic file attributes
			FileAttributes fileAttributes = Win32Helper.GetFileAttributes(itemPath);
			ViewModel.IsReadOnly = fileAttributes.HasFlag(FileAttributes.ReadOnly);
			ViewModel.IsHidden = fileAttributes.HasFlag(FileAttributes.Hidden);
			ViewModel.CanCompressContent = Win32Helper.CanCompressContent(itemPath);
			ViewModel.ItemSizeVisibility = true;
			ViewModel.ItemSize = Item.FileSizeBytes.ToLongSizeString();

			// Only check the compressed attribute and size on disk for items on the device
			if (isOnDevice)
			{
				ViewModel.IsContentCompressed = fileAttributes.HasFlag(FileAttributes.Compressed);
				ViewModel.ItemSizeOnDisk = Win32Helper.GetFileSizeOnDisk(itemPath)?.ToLongSizeString() ?? string.Empty;
			}

			// Load icon
			var result = await FileThumbnailHelper.GetIconAsync(
				itemPath,
				Constants.ShellIconSizes.ExtraLarge,
				false,
				IconOptions.None);

			if (result is not null)
			{
				ViewModel.IconData = result;
				ViewModel.LoadUnknownTypeGlyph = false;
				ViewModel.LoadFileIcon = true;
			}

			// Handle shortcut properties
			if (Item.IsShortcut)
			{
				ViewModel.ItemCreatedTimestampReal = Item.ItemDateCreatedReal;
				ViewModel.ItemAccessedTimestampReal = Item.ItemDateAccessedReal;

				if (Item.IsLinkItem || string.IsNullOrWhiteSpace(((IShortcutItem)Item).TargetPath))
				{
					// Can't show any other property
					return;
				}
			}

			// Get file for further processing
			var targetPath = (Item as IShortcutItem)?.TargetPath;
			string filePath = !string.IsNullOrEmpty(targetPath) ? targetPath : itemPath;
			var shellViewModel = AppInstance.GetRequiredShellViewModel();

			// Couldn't access the file and can't load any other properties
			var fileResult = await shellViewModel.GetFileFromPathAsync(filePath);
			if (fileResult.Result is not { } file)
				return;

			// Can't load any other properties
			if (Item.IsShortcut)
				return;

			// Load uncompressed size for browsable zip files on device
			if (isOnDevice && FileExtensionHelpers.IsBrowsableZipFile(Item.FileExtension, out _))
			{
				if (await ZipStorageFolder.FromPathAsync(itemPath) is ZipStorageFolder zipFolder)
				{
					var uncompressedSize = await zipFolder.GetUncompressedSize();
					ViewModel.UncompressedItemSize = uncompressedSize.ToLongSizeString();
					ViewModel.UncompressedItemSizeBytes = uncompressedSize;
				}
			}

			// Get other properties if available
			if (file.Properties is not null)
				_ = GetOtherPropertiesAsync(file.Properties);
#endif
		}

		public async Task GetSystemFilePropertiesAsync()
		{
			var itemPath = Item.GetRequiredPath();

			if (OperatingSystem.IsLinux())
			{
				GetSystemFilePropertiesLinux(itemPath);
				return;
			}

			var fileResult = await FilesystemTasks.WrapNullable(() => StorageFileExtensions.DangerousGetFileFromPathAsync(itemPath));
			if (fileResult.Result is not { } file)
			{
				// Could not access file, can't show any other property
				return;
			}

			var list = await FileProperty.RetrieveAndInitializePropertiesAsync(file);

			var addressProperty = list.Find(x => x.ID == "address")
				?? throw new InvalidOperationException("The file property definitions do not contain the address property.");
			var latitudeProperty = list.Find(x => x.Property == "System.GPS.LatitudeDecimal")
				?? throw new InvalidOperationException("The file property definitions do not contain the latitude property.");
			var longitudeProperty = list.Find(x => x.Property == "System.GPS.LongitudeDecimal")
				?? throw new InvalidOperationException("The file property definitions do not contain the longitude property.");
			addressProperty.Value = await LocationHelpers.GetAddressFromCoordinatesAsync(
				(double?)latitudeProperty.Value,
				(double?)longitudeProperty.Value);

			var query = list
				.Where(fileProp => !(fileProp.Value is null && fileProp.IsReadOnly))
				.GroupBy(fileProp => fileProp.SectionResource!)
				.Select(group => new FilePropertySection(group) { Key = group.Key })
				.Where(section => !section.All(fileProp => fileProp.Value is null)
					|| FileProperty.IsSectionApplicableForEmpty(section.Key, Item.FileExtension))
				.OrderBy(group => group.Priority);

			ViewModel.PropertySections = new ObservableCollection<FilePropertySection>(query);
			ViewModel.FileProperties = new ObservableCollection<FileProperty>(list.Where(i => i.Value is not null));
		}

		private void GetSystemFilePropertiesLinux(string itemPath)
		{
			var list = LinuxFileMetadata.Read(itemPath);
			var sections = list
				.GroupBy(p => p.SectionResource!)
				.Select(group => new FilePropertySection(group) { Key = group.Key })
				.OrderBy(section => section.Priority);

			ViewModel.PropertySections = new ObservableCollection<FilePropertySection>(sections);
			ViewModel.FileProperties = new ObservableCollection<FileProperty>(list);
		}

		public async Task SyncPropertyChangesAsync()
		{
			if (OperatingSystem.IsLinux())
				return;

			// Couldn't access the file to save properties
			var itemPath = Item.GetRequiredPath();
			var fileResult = await FilesystemTasks.WrapNullable(() => StorageFileExtensions.DangerousGetFileFromPathAsync(itemPath));
			if (fileResult.Result is not { } file)
				return;

			var failedProperties = "";

			foreach (var group in ViewModel.PropertySections)
			{
				foreach (FileProperty prop in group)
				{
					if (!prop.IsReadOnly && prop.Modified)
					{
						var propertyName = prop.Property
							?? throw new InvalidOperationException("An editable file property does not have an identifier.");
						var newDict = new Dictionary<string, object?>
						{
							{ propertyName, prop.Value }
						};

						try
						{
							if (file.Properties is not null)
							{
								await file.Properties.SavePropertiesAsync(newDict);
							}
						}
						catch
						{
							failedProperties += $"{prop.Name}\n";
						}
					}
				}
			}

			if (!string.IsNullOrWhiteSpace(failedProperties))
			{
				throw new Exception($"The following properties failed to save: {failedProperties}");
			}
		}

		public async Task ClearPropertiesAsync()
		{
			if (OperatingSystem.IsLinux())
				return;

			var failedProperties = new List<string>();
			var itemPath = Item.GetRequiredPath();
			var fileResult = await FilesystemTasks.WrapNullable(() => StorageFileExtensions.DangerousGetFileFromPathAsync(itemPath));
			if (fileResult.Result is not { } file)
				return;

			foreach (var group in ViewModel.PropertySections)
			{
				foreach (FileProperty prop in group)
				{
					if (!prop.IsReadOnly)
					{
						var propertyName = prop.Property
							?? throw new InvalidOperationException("An editable file property does not have an identifier.");
						var newDict = new Dictionary<string, object?>
						{
							{ propertyName, null }
						};

						try
						{
							if (file.Properties is not null)
							{
								await file.Properties.SavePropertiesAsync(newDict);
							}
						}
						catch
						{
							failedProperties.Add(prop.Name);
						}
					}
				}
			}

			_ = GetSystemFilePropertiesAsync();
		}

#if WINDOWS
		private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (OperatingSystem.IsLinux())
				return;

			var itemPath = Item.GetRequiredPath();

			switch (e.PropertyName)
			{
				case nameof(ViewModel.IsReadOnly):
					if (ViewModel.IsReadOnly is not null)
					{
						if ((bool)ViewModel.IsReadOnly)
							Win32Helper.SetFileAttribute(itemPath, System.IO.FileAttributes.ReadOnly);
						else
							Win32Helper.UnsetFileAttribute(itemPath, System.IO.FileAttributes.ReadOnly);
					}

					break;

				case nameof(ViewModel.IsHidden):
					if (ViewModel.IsHidden is not null)
					{
						if ((bool)ViewModel.IsHidden)
							Win32Helper.SetFileAttribute(itemPath, System.IO.FileAttributes.Hidden);
						else
							Win32Helper.UnsetFileAttribute(itemPath, System.IO.FileAttributes.Hidden);
					}

					break;

				case nameof(ViewModel.IsContentCompressed):
					Win32Helper.SetCompressionAttributeIoctl(itemPath, ViewModel.IsContentCompressed ?? false);
					break;

				case nameof(ViewModel.RunAsAdmin):
				case nameof(ViewModel.ShortcutItemPath):
				case nameof(ViewModel.ShortcutItemWorkingDir):
				case nameof(ViewModel.ShowWindowCommand):
				case nameof(ViewModel.ShortcutItemArguments):
					if (string.IsNullOrWhiteSpace(ViewModel.ShortcutItemPath))
						return;

					await FileOperationsHelpers.CreateOrUpdateLinkAsync(itemPath, ViewModel.ShortcutItemPath, ViewModel.ShortcutItemArguments, ViewModel.ShortcutItemWorkingDir, ViewModel.RunAsAdmin, (Windows.Win32.UI.WindowsAndMessaging.SHOW_WINDOW_CMD)(int)ViewModel.ShowWindowCommand);

					break;
			}
		}
#endif
	}
}
