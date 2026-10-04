// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Specialized;

namespace Files.App.Services.Desktop
{
	// LINUX-TODO(windows-services): no-op implementations of the Windows-only service contracts so existing consumers keep working.
	// Replace with real Linux backends where a feature makes sense (recent items: recently-used.xbel, wallpaper: gsettings/portal).

	internal sealed class DesktopSecurityService : IWindowsSecurityService
	{
		public bool IsAppElevated() => Environment.UserName == "root";

		public bool CanDragAndDrop() => true;

		public bool IsElevationRequired(string? path) => false;
	}

	internal sealed class DesktopRecentItemsService : IWindowsRecentItemsService
	{
		public IReadOnlyList<RecentItem> RecentFiles { get; } = [];

		public IReadOnlyList<RecentItem> RecentFolders { get; } = [];

		public event EventHandler<NotifyCollectionChangedEventArgs>? RecentFilesChanged { add { } remove { } }

		public event EventHandler<NotifyCollectionChangedEventArgs>? RecentFoldersChanged { add { } remove { } }

		public Task<bool> UpdateRecentFilesAsync() => Task.FromResult(true);

		public Task<bool> UpdateRecentFoldersAsync() => Task.FromResult(true);

		public bool Add(string path) => false;

		public bool Remove(RecentItem item) => false;

		public bool Clear() => false;
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
}
