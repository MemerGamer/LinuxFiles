// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Specialized;

namespace Files.App.Services.Desktop
{
	// LINUX-TODO(windows-services): no-op implementations of the Windows-only service contracts so existing consumers keep working.
	// Replace with real Linux backends where a feature makes sense (wallpaper: gsettings/portal). Recent items live in DesktopRecentItemsService.

	internal sealed class DesktopSecurityService : IWindowsSecurityService
	{
		public bool IsAppElevated() => Environment.UserName == "root";

		public bool CanDragAndDrop() => true;

		public bool IsElevationRequired(string? path) => false;
	}

	internal sealed class DesktopIniService : IWindowsIniService
	{
		public List<IniSectionDataItem> GetData(string filePath) => [];
	}

	internal sealed class DesktopWallpaperService : IWindowsWallpaperService
	{
		public void SetDesktopWallpaper(string szPath) { }

		public void SetDesktopSlideshow(string[] aszPaths) { }

		public Task SetLockScreenWallpaper(string szPath) => Task.CompletedTask;
	}

	internal sealed class DesktopCompatibilityService : IWindowsCompatibilityService
	{
		public WindowsCompatibilityOptions GetCompatibilityOptionsForPath(string filePath) => new();

		public bool SetCompatibilityOptionsForPath(string filePath, WindowsCompatibilityOptions options) => false;
	}

	// LINUX-TODO(new-item): back the "New" menu with ITemplatesService (user Templates folder) instead of the ShellNew entries
	internal sealed class DesktopAddItemService : IAddItemService
	{
		public Task InitializeAsync() => Task.CompletedTask;

		public List<ShellNewEntry>? GetEntries() => [];
	}

	internal sealed class DesktopJumpListService : IWindowsJumpListService
	{
		public Task InitializeAsync() => Task.CompletedTask;

		public Task AddFolderAsync(string path) => Task.CompletedTask;

		public Task RefreshPinnedFoldersAsync() => Task.CompletedTask;

		public Task RemoveFolderAsync(string path) => Task.CompletedTask;

		public Task<IEnumerable<string>> GetFoldersAsync() => Task.FromResult<IEnumerable<string>>([]);
	}

	// LINUX-TODO(pickers): open file/folder choosers through the xdg-desktop-portal FileChooser; until then pickers report "cancelled"
	internal sealed class DesktopCommonDialogService : ICommonDialogService
	{
		public bool Open_FileOpenDialog(nint hWnd, bool pickFoldersOnly, string[] filters, Environment.SpecialFolder defaultFolder, out string filePath, Guid? clientGuid = null)
		{
			filePath = string.Empty;
			return false;
		}

		public bool Open_FileSaveDialog(nint hWnd, bool pickFoldersOnly, string[] filters, Environment.SpecialFolder defaultFolder, out string filePath)
		{
			filePath = string.Empty;
			return false;
		}

		public bool Open_NetworkConnectionDialog(nint hWnd, bool hideRestoreConnectionCheckBox = false, bool persistConnectionAtLogon = false, bool readOnlyPath = false, string? remoteNetworkName = null, bool useMostRecentPath = false) => false;
	}

	// Linux has no Start Menu pins
	internal sealed class DesktopStartMenuService : IStartMenuService
	{
		[Obsolete("Use IsPinnedAsync instead. This method is used for a workaround in ListedItem class to avoid major refactoring.")]
		public bool IsPinned(string itemPath) => false;

		public Task<bool> IsPinnedAsync(IStorable storable) => Task.FromResult(false);

		public Task PinAsync(IStorable storable, string? displayName = null) => Task.CompletedTask;

		public Task UnpinAsync(IStorable storable) => Task.CompletedTask;
	}
}
