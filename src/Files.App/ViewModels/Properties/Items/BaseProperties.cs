// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Mime;
using Files.Platform.Abstractions.Permissions;
using Microsoft.UI.Dispatching;
using System.IO;
using Windows.Storage.FileProperties;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using FileAttributes = System.IO.FileAttributes;

namespace Files.App.ViewModels.Properties
{
	public abstract class BaseProperties
	{
		public IShellPage AppInstance { get; }

		public SelectedItemsPropertiesViewModel ViewModel { get; }

		public CancellationTokenSource TokenSource { get; }

		public DispatcherQueue Dispatcher { get; }

		protected BaseProperties(
			SelectedItemsPropertiesViewModel viewModel,
			CancellationTokenSource tokenSource,
			DispatcherQueue dispatcher,
			IShellPage appInstance)
		{
			ViewModel = viewModel;
			TokenSource = tokenSource;
			Dispatcher = dispatcher;
			AppInstance = appInstance;
		}

		public abstract void GetBaseProperties();

		public abstract Task GetSpecialPropertiesAsync();

		private static unsafe (FindCloseSafeHandle Handle, WIN32_FIND_DATAW Data) FindFirstFile(string path)
		{
			WIN32_FIND_DATAW findData = default;
			FindCloseSafeHandle hFile = PInvoke.FindFirstFileEx(
				path,
				FINDEX_INFO_LEVELS.FindExInfoBasic,
				&findData,
				FINDEX_SEARCH_OPS.FindExSearchNameMatch,
				FIND_FIRST_EX_FLAGS.FIND_FIRST_EX_LARGE_FETCH);

			return (hFile, findData);
		}

		public async Task GetOtherPropertiesAsync(IStorageItemExtraProperties properties)
		{
			string dateAccessedProperty = "System.DateAccessed";
			string dateModifiedProperty = "System.DateModified";

			List<string> propertiesName =
			[
				dateAccessedProperty,
				dateModifiedProperty
			];

			IDictionary<string, object> extraProperties = await properties.RetrievePropertiesAsync(propertiesName);

			// Cannot get date and owner in MTP devices
			ViewModel.ItemAccessedTimestampReal = (DateTimeOffset)(extraProperties[dateAccessedProperty] ?? DateTimeOffset.Now);
			ViewModel.ItemModifiedTimestampReal = (DateTimeOffset)(extraProperties[dateModifiedProperty] ?? DateTimeOffset.Now);
		}

		public async Task<(long size, long sizeOnDisk)> CalculateFolderSizeAsync(string path, CancellationToken token)
		{
			if (OperatingSystem.IsLinux())
				return await CalculateFolderSizeLinuxAsync(path, token);

			if (string.IsNullOrEmpty(path))
			{
				// In MTP devices calculating folder size would be too slow
				// Also should use StorageFolder methods instead of FindFirstFileEx
				return (0, 0);
			}

			long size = 0;
			long sizeOnDisk = 0;
			var (hFile, findData) = FindFirstFile(path + "\\*.*");
			using FindCloseSafeHandle findHandleScope = hFile;

			var count = 0;
			if (!hFile.IsInvalid)
			{
				do
				{
					string fileName = findData.cFileName.ToString();
					if (((FileAttributes)findData.dwFileAttributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
						// Skip symbolic links and junctions
						continue;

					if (((FileAttributes)findData.dwFileAttributes & FileAttributes.Directory) != FileAttributes.Directory)
					{
						size += findData.GetSize();
						var fileSizeOnDisk = Win32Helper.GetFileSizeOnDisk(Path.Combine(path, fileName));
						sizeOnDisk += fileSizeOnDisk ?? 0;
						++count;
						ViewModel.FilesCount++;
					}
					else if (fileName != "." && fileName != "..")
					{
						var itemPath = Path.Combine(path, fileName);

						var folderSize = await CalculateFolderSizeAsync(itemPath, token);
						size += folderSize.size;
						sizeOnDisk += folderSize.sizeOnDisk;
						++count;
						ViewModel.FoldersCount++;
					}

					if (size > ViewModel.ItemSizeBytes || sizeOnDisk > ViewModel.ItemSizeOnDiskBytes)
					{
						await Dispatcher.EnqueueOrInvokeAsync(() =>
						{
							ViewModel.ItemSizeBytes = size;
							ViewModel.ItemSize = size.ToSizeString();
							ViewModel.ItemSizeOnDiskBytes = sizeOnDisk;
							ViewModel.ItemSizeOnDisk = sizeOnDisk.ToSizeString();
							SetItemsCountString();
						},
						DispatcherQueuePriority.Low);
					}

					if (token.IsCancellationRequested)
						break;
				}
				while (PInvoke.FindNextFile(hFile, out findData));

				return (size, sizeOnDisk);
			}
			else
			{
				return (0, 0);
			}
		}

		private async Task<(long size, long sizeOnDisk)> CalculateFolderSizeLinuxAsync(string path, CancellationToken token)
		{
			if (string.IsNullOrEmpty(path))
				return (0, 0);

			int reportedFiles = 0, reportedFolders = 0;

			void Report(FolderScanTotals totals)
			{
				ViewModel.FilesCount += totals.Files - reportedFiles;
				ViewModel.FoldersCount += totals.Folders - reportedFolders;
				reportedFiles = totals.Files;
				reportedFolders = totals.Folders;

				if (totals.Size > ViewModel.ItemSizeBytes || totals.SizeOnDisk > ViewModel.ItemSizeOnDiskBytes)
				{
					ViewModel.ItemSizeBytes = totals.Size;
					ViewModel.ItemSize = totals.Size.ToSizeString();
					ViewModel.ItemSizeOnDiskBytes = totals.SizeOnDisk;
					ViewModel.ItemSizeOnDisk = totals.SizeOnDisk.ToSizeString();
				}

				SetItemsCountString();
			}

			var progress = new DispatcherProgress(Dispatcher, Report);
			var result = await Ioc.Default.GetRequiredService<IFileStatService>().ScanFolderAsync(path, progress, token);
			await Dispatcher.EnqueueOrInvokeAsync(() => Report(result), DispatcherQueuePriority.Low);

			return (result.Size, result.SizeOnDisk);
		}

		private sealed class DispatcherProgress(DispatcherQueue dispatcher, Action<FolderScanTotals> handler) : IProgress<FolderScanTotals>
		{
			public void Report(FolderScanTotals value)
				=> _ = dispatcher.EnqueueOrInvokeAsync(() => handler(value), DispatcherQueuePriority.Low);
		}

		/// <summary>
		/// Fills the stat-derived fields (sizes, timestamps, link target) of the General page on Linux and hides the Windows attributes.
		/// </summary>
		protected void ApplyLinuxStat(string path)
		{
			ViewModel.ItemAttributesVisibility = false;
			ViewModel.IsDownloadedFile = false;
			ViewModel.CanCompressContent = false;

			if (!Ioc.Default.GetRequiredService<IFileStatService>().TryGetStat(path, out var stat))
				return;

			ViewModel.ItemModifiedTimestampReal = stat.Modified;
			ViewModel.ItemAccessedTimestampReal = stat.Accessed;
			if (stat.Created is { } created)
				ViewModel.ItemCreatedTimestampReal = created;
			else
				ViewModel.ItemCreatedTimestampVisibility = false;

			if (!stat.IsDirectory)
			{
				ViewModel.ItemSizeVisibility = true;
				ViewModel.ItemSizeBytes = stat.Size;
				ViewModel.ItemSize = stat.Size.ToLongSizeString();
				ViewModel.ItemSizeOnDisk = stat.SizeOnDisk.ToLongSizeString();
			}

			if (stat.IsSymbolicLink)
				ViewModel.LinkTarget = stat.LinkTarget;
		}

		/// <summary>
		/// Sets the type description from the MIME database.
		/// </summary>
		protected async Task ApplyLinuxTypeAsync(string path)
		{
			try
			{
				var mime = Ioc.Default.GetRequiredService<IMimeTypeService>();
				var mimeType = await mime.GetMimeTypeAsync(path, TokenSource.Token);
				var description = await mime.GetDescriptionAsync(mimeType, TokenSource.Token);
				if (!string.IsNullOrEmpty(description))
					ViewModel.ItemType = description;
				else if (string.IsNullOrEmpty(ViewModel.ItemType))
					ViewModel.ItemType = mimeType;
			}
			catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
			{
			}
		}

		public void SetItemsCountString()
		{
			ViewModel.FilesAndFoldersCountString = ViewModel.LocationsCount > 0
				? Strings.PropertiesFilesAndFoldersAndLocationsCount.GetLocalizedFormatResource(ViewModel.FilesCount, ViewModel.FoldersCount, ViewModel.LocationsCount)
				: Strings.PropertiesFilesAndFoldersCountString.GetLocalizedFormatResource(ViewModel.FilesCount, ViewModel.FoldersCount);
		}
	}
}
