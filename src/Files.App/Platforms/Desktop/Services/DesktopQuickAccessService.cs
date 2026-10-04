// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions;
using System.IO;
using System.Text.Json;

namespace Files.App.Services.Desktop
{
	/// <summary>
	/// Quick access (pinned sidebar folders) persisted as a JSON list under the app's XDG settings directory.
	/// </summary>
	/// <remarks>
	/// LINUX-TODO(quickaccess): also merge GTK bookmarks (~/.config/gtk-3.0/bookmarks).
	/// </remarks>
	internal sealed class DesktopQuickAccessService : IQuickAccessService
	{
		private readonly object _lock = new();
		private readonly IAppDataPaths _paths = Ioc.Default.GetRequiredService<IAppDataPaths>();
		private readonly IUserDirectories _userDirectories = Ioc.Default.GetRequiredService<IUserDirectories>();

		private string FilePath => Path.Combine(_paths.SettingsDirectory, "pinned_folders.json");

		private List<string> Read()
		{
			lock (_lock)
			{
				try
				{
					if (File.Exists(FilePath))
						return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? [];
				}
				catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
				{
				}

				// First run: the usual XDG places
				var defaults = new List<string>();
				foreach (var dir in new[] { _userDirectories.Desktop, _userDirectories.Downloads, _userDirectories.Documents, _userDirectories.Pictures, _userDirectories.Music, _userDirectories.Videos })
				{
					if (Directory.Exists(dir) && !defaults.Contains(dir))
						defaults.Add(dir);
				}

				return defaults;
			}
		}

		private void Write(IEnumerable<string> folders)
		{
			lock (_lock)
			{
				Directory.CreateDirectory(_paths.SettingsDirectory);
				var tmp = FilePath + ".tmp";
				File.WriteAllText(tmp, JsonSerializer.Serialize(folders.ToList()));
				File.Move(tmp, FilePath, true);
			}
		}

		public Task<IEnumerable<ShellFileItem>> GetPinnedFoldersAsync()
		{
			IEnumerable<ShellFileItem> result = Read().Select(path => new ShellFileItem
			{
				IsFolder = true,
				FilePath = path,
				FileName = Path.GetFileName(path.TrimEnd('/')),
				Properties = new() { ["System.Home.IsPinned"] = true }
			}).ToList();

			return Task.FromResult(result);
		}

		public Task PinToSidebarAsync(string folderPath) => PinToSidebarAsync([folderPath]);

		public Task PinToSidebarAsync(string[] folderPaths) => PinToSidebarAsync(folderPaths, true);

		private async Task PinToSidebarAsync(string[] folderPaths, bool doUpdateQuickAccessWidget)
		{
			var pinned = Read();
			foreach (var folderPath in folderPaths)
			{
				if (!pinned.Contains(folderPath))
					pinned.Add(folderPath);
			}
			Write(pinned);

			await App.QuickAccessManager.Model.LoadAsync();
			if (doUpdateQuickAccessWidget)
				App.QuickAccessManager.UpdateQuickAccessWidget?.Invoke(this, new ModifyQuickAccessEventArgs(folderPaths, true));
		}

		public Task UnpinFromSidebarAsync(string folderPath) => UnpinFromSidebarAsync([folderPath]);

		public Task UnpinFromSidebarAsync(string[] folderPaths) => UnpinFromSidebarAsync(folderPaths, true);

		private async Task UnpinFromSidebarAsync(string[] folderPaths, bool doUpdateQuickAccessWidget)
		{
			var pinned = Read();
			if (folderPaths.Length == 0)
				pinned.Clear();
			else
				pinned.RemoveAll(folderPaths.Contains);
			Write(pinned);

			await App.QuickAccessManager.Model.LoadAsync();
			if (doUpdateQuickAccessWidget)
				App.QuickAccessManager.UpdateQuickAccessWidget?.Invoke(this, new ModifyQuickAccessEventArgs(folderPaths, false));
		}

		public bool IsItemPinned(string folderPath)
			=> App.QuickAccessManager.Model.PinnedFolders.Contains(folderPath);

		public async Task SaveAsync(string[] items)
		{
			Write(items);

			await App.QuickAccessManager.Model.LoadAsync();
			App.QuickAccessManager.UpdateQuickAccessWidget?.Invoke(this, new ModifyQuickAccessEventArgs(items, true)
			{
				Reorder = true
			});
		}
	}
}
