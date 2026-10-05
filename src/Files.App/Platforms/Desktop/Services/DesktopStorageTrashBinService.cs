// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Trash;

namespace Files.App.Services.Desktop
{
	/// <summary>
	/// <see cref="IStorageTrashBinService"/> backed by the freedesktop.org trash through <see cref="ITrashService"/>.
	/// </summary>
	internal sealed class DesktopStorageTrashBinService : IStorageTrashBinService
	{
		private readonly ITrashService _trash = Ioc.Default.GetRequiredService<ITrashService>();

		// LINUX-TODO(trash): bridge ITrashService.Watcher into RecycleBinWatcher (its Windows-only watcher is inert on Linux)
		public RecycleBinWatcher Watcher { get; } = new();

		public async Task<List<ShellFileItem>> GetAllRecycleBinFoldersAsync()
		{
			var items = await _trash.ListAsync();
			return items.Select(item => new ShellFileItem
			{
				IsFolder = item.IsDirectory,
				RecyclePath = item.TrashedPath,
				FileName = item.Name,
				FilePath = item.OriginalPath,
				RecycleDate = item.DeletionDate.LocalDateTime,
				ModifiedDate = item.DeletionDate.LocalDateTime,
				CreatedDate = item.DeletionDate.LocalDateTime,
				FileSize = item.Size.ToSizeString(),
				FileSizeBytes = (ulong)Math.Max(0, item.Size),
			}).ToList();
		}

		public (bool HasRecycleBin, long NumItems, long BinSize) QueryRecycleBin(string drive = "")
		{
			var items = Task.Run(() => _trash.ListAsync()).GetAwaiter().GetResult();
			return (true, items.Count, items.Sum(i => i.Size));
		}

		public ulong GetSize() => (ulong)Task.Run(() => _trash.GetSizeAsync()).GetAwaiter().GetResult();

		public bool HasItems() => Task.Run(() => _trash.HasItemsAsync()).GetAwaiter().GetResult();

		public bool IsUnderTrashBin(string? path) => _trash.IsUnderTrash(path);

		public Task<bool> CanGoTrashBin(string? path) => Task.FromResult(_trash.IsSupported(path));

		public bool EmptyTrashBin()
		{
			Task.Run(() => _trash.EmptyAsync()).GetAwaiter().GetResult();
			return true;
		}

		public async Task<bool> RestoreAllTrashesAsync()
		{
			var items = await _trash.ListAsync();
			var results = await _trash.RestoreAsync(items);
			var warned = results.Where(r => r.Succeeded && !string.IsNullOrEmpty(r.ErrorMessage)).ToList();
			if (warned.Count > 0)
				StatusCenterHelper.AddCard_RestoreWarning(warned.Select(r => r.ResultPath ?? r.Item?.OriginalPath).OfType<string>());
			return results.All(r => r.Succeeded);
		}
	}
}
