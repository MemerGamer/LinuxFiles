// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.FileChooser;
using Microsoft.Extensions.Logging;
using System.Collections.Specialized;
using System.Threading;

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

	// Prefer the Async methods: the synchronous ones block the calling thread while the chooser is open.
	internal sealed class DesktopCommonDialogService : ICommonDialogService
	{
		private readonly IFileChooserService _fileChooser;
		private int _unavailableLogged;

		public DesktopCommonDialogService(IFileChooserService fileChooser)
		{
			_fileChooser = fileChooser;
		}

		public bool Open_FileOpenDialog(nint hWnd, bool pickFoldersOnly, string[] filters, Environment.SpecialFolder defaultFolder, out string filePath, Guid? clientGuid = null)
		{
			// Synchronous fallback for callers that cannot await; it blocks the calling thread
			var (result, path) = Task.Run(() => OpenFileOpenDialogAsync(hWnd, pickFoldersOnly, filters, defaultFolder, clientGuid)).GetAwaiter().GetResult();
			filePath = path;
			return result;
		}

		public bool Open_FileSaveDialog(nint hWnd, bool pickFoldersOnly, string[] filters, Environment.SpecialFolder defaultFolder, out string filePath)
		{
			var (result, path) = Task.Run(() => OpenFileSaveDialogAsync(hWnd, pickFoldersOnly, filters, defaultFolder)).GetAwaiter().GetResult();
			filePath = path;
			return result;
		}

		public Task<(bool Result, string FilePath)> OpenFileOpenDialogAsync(nint hWnd, bool pickFoldersOnly, string[] filters, Environment.SpecialFolder defaultFolder, Guid? clientGuid = null)
			=> ChooseAsync(new FileChooserRequest { PickFolder = pickFoldersOnly, Filters = ParseFilters(filters), ParentWindowId = (ulong)hWnd });

		public Task<(bool Result, string FilePath)> OpenFileSaveDialogAsync(nint hWnd, bool pickFoldersOnly, string[] filters, Environment.SpecialFolder defaultFolder)
		{
			var folder = Environment.GetFolderPath(defaultFolder);
			return ChooseAsync(new FileChooserRequest
			{
				Save = true,
				PickFolder = pickFoldersOnly,
				Filters = ParseFilters(filters),
				CurrentFolder = string.IsNullOrEmpty(folder) ? null : folder,
				ParentWindowId = (ulong)hWnd,
			});
		}

		public bool Open_NetworkConnectionDialog(nint hWnd, bool hideRestoreConnectionCheckBox = false, bool persistConnectionAtLogon = false, bool readOnlyPath = false, string? remoteNetworkName = null, bool useMostRecentPath = false) => false;

		private async Task<(bool Result, string FilePath)> ChooseAsync(FileChooserRequest request)
		{
			var result = await _fileChooser.ChooseAsync(request).ConfigureAwait(true);
			if (result.Status == FileChooserStatus.Unavailable && Interlocked.Exchange(ref _unavailableLogged, 1) == 0)
				App.Logger.LogWarning("The xdg-desktop-portal FileChooser is not available; file pickers will report cancelled.");

			return result.Status == FileChooserStatus.Selected && result.Paths.Count > 0
				? (true, result.Paths[0])
				: (false, string.Empty);
		}

		// Filters come as [name, "*.a;*.b", name, ...] pairs
		private static List<FileChooserFilter> ParseFilters(string[] filters)
		{
			var list = new List<FileChooserFilter>();
			for (var i = 0; i + 1 < filters.Length; i += 2)
			{
				var patterns = filters[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
				if (patterns.Length > 0)
					list.Add(new FileChooserFilter(filters[i], patterns));
			}

			return list;
		}
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
