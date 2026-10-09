// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Platform.Abstractions.Archives;
using Files.Platform.Abstractions.FileOperations;
using Files.Shared.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Windows.Storage;

namespace Files.App.Services
{
	/// <summary>
	/// Desktop (Uno) implementation of <see cref="IStorageArchiveService"/> on top of the managed, platform neutral <see cref="IArchiveService"/>.
	/// </summary>
	/// <inheritdoc cref="IStorageArchiveService"/>
	public class StorageArchiveService : IStorageArchiveService
	{
		private StatusCenterViewModel StatusCenterViewModel { get; } = Ioc.Default.GetRequiredService<StatusCenterViewModel>();

		private IArchiveService ArchiveService { get; } = Ioc.Default.GetRequiredService<IArchiveService>();

		private readonly ConcurrentDictionary<string, byte> inProgressArchives = new(StringComparer.Ordinal);

		/// <inheritdoc/>
		public event EventHandler<string>? CompressionCompleted;

		/// <inheritdoc/>
		public bool IsCompressionInProgress(string archivePath)
			=> inProgressArchives.ContainsKey(archivePath);

		/// <inheritdoc/>
		public bool CanCompress(IReadOnlyList<ListedItem> items)
			=> items.Count > 0 && (!CanDecompress(items) || items.Count > 1);

		/// <inheritdoc/>
		public bool CanDecompress(IReadOnlyList<ListedItem> items)
		{
			return
				items.Count > 0 &&
				items.All(x => x.PrimaryItemAttribute == StorageItemTypes.File && !string.IsNullOrEmpty(x.ItemPath) && ArchiveService.IsArchiveFileName(x.ItemPath));
		}

		/// <inheritdoc/>
		public async Task<bool> CompressAsync(ICompressArchiveModel compressionModel)
		{
			var archivePath = compressionModel.GetArchivePath();

			int index = 1;
			while (SystemIO.File.Exists(archivePath) || SystemIO.Directory.Exists(archivePath))
				archivePath = compressionModel.GetArchivePath($" ({++index})");

			compressionModel.ArchivePath = archivePath;
			var sources = compressionModel.Sources.ToArray();

			var banner = StatusCenterHelper.AddCard_Compress(
				sources,
				archivePath.CreateEnumerable(),
				ReturnResult.InProgress,
				sources.Length);

			compressionModel.Progress = banner.ProgressEventSource;
			compressionModel.CancellationToken = banner.CancellationToken;

			StatusCenterItemProgressModel fsProgress = new(banner.ProgressEventSource, false, FileSystemStatusCode.InProgress);
			fsProgress.Report(0);

			inProgressArchives.TryAdd(archivePath, 0);

			ArchiveResult result;
			var skippedPrompt = false;
			try
			{
				result = await ArchiveService.CreateAsync(
					sources,
					archivePath,
					new ArchiveCreateOptions
					{
						Format = ToPlatformFormat(compressionModel.FileFormat),
						Level = ToPlatformLevel(compressionModel.CompressionLevel),
						Progress = new Progress<ArchiveProgress>(p =>
						{
							fsProgress.ItemsCount = p.EntriesTotal;
							fsProgress.TotalSize = p.BytesTotal;
							fsProgress.EnumerationCompleted = true;
							fsProgress.FileName = p.CurrentEntry;
							fsProgress.SetProcessedSize(p.BytesProcessed);
							fsProgress.Report(p.BytesTotal > 0 ? p.BytesProcessed / (double)p.BytesTotal * 100 : null);
						}),
						ConfirmSkipped = async (skipped, ct) =>
						{
							App.Logger.LogWarning("Skipped {Count} item(s) that could not be archived.", skipped.Count);
							skippedPrompt = true;
							var dialogService = Ioc.Default.GetRequiredService<IDialogService>();
							var dialogResult = await RunOnUIAsync(() => dialogService.ShowDialogAsync(new CompressSkippedItemsDialogViewModel([.. skipped])));
							return dialogResult is DialogResult.Primary;
						},
					},
					banner.CancellationToken);
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Error compressing items.");
				result = new ArchiveResult(false, false, 0, 0, ex.Message);
			}
			finally
			{
				inProgressArchives.TryRemove(archivePath, out _);
			}

			StatusCenterViewModel.RemoveItem(banner);

			StatusCenterHelper.AddCard_Compress(
				sources,
				archivePath.CreateEnumerable(),
				result.Succeeded ? ReturnResult.Success : result.Cancelled || banner.CancellationToken.IsCancellationRequested ? ReturnResult.Cancelled : ReturnResult.Failed,
				sources.Length);

			if (result.Succeeded)
				CompressionCompleted?.Invoke(this, archivePath);
			else if (!result.Cancelled && !skippedPrompt)
				App.Logger.LogWarning("Compression failed: {Error}", result.Error);

			return result.Succeeded;
		}

		/// <inheritdoc/>
		public async Task<bool> DecompressAsync(string archiveFilePath, string destinationFolderPath, string password = "", Encoding? encoding = null)
		{
			if (string.IsNullOrEmpty(archiveFilePath) || string.IsNullOrEmpty(destinationFolderPath))
				return false;

			var statusCard = StatusCenterHelper.AddCard_Decompress(
				archiveFilePath.CreateEnumerable(),
				destinationFolderPath.CreateEnumerable(),
				ReturnResult.InProgress);

			StatusCenterItemProgressModel fsProgress = new(statusCard.ProgressEventSource, false, FileSystemStatusCode.InProgress);
			fsProgress.Report(0);

			ArchiveResult result;
			try
			{
				result = await ArchiveService.ExtractAsync(
					archiveFilePath,
					destinationFolderPath,
					new ArchiveExtractOptions
					{
						Password = string.IsNullOrEmpty(password) ? null : password,
						FileNameEncoding = encoding,
						Progress = new Progress<ArchiveProgress>(p =>
						{
							fsProgress.ItemsCount = p.EntriesTotal;
							fsProgress.TotalSize = p.BytesTotal;
							fsProgress.EnumerationCompleted = true;
							fsProgress.FileName = p.CurrentEntry;
							fsProgress.SetProcessedSize(p.BytesProcessed);
							fsProgress.Report(p.BytesTotal > 0 ? Math.Min(p.BytesProcessed / (double)p.BytesTotal * 100, 100) : null);
						}),
						LimitExceeded = (violation, _) => RunOnUIAsync(() => ConfirmLimitAsync(violation)),
						ResolveConflict = (conflict, _) => RunOnUIAsync(() => ResolveConflictAsync(conflict)),
					},
					statusCard.CancellationToken);
			}
			catch (ArchivePasswordException ex)
			{
				App.Logger.LogWarning(ex, "Archive password missing or wrong.");
				result = new ArchiveResult(false, false, 0, 0, ex.Message);
			}
			catch (Exception ex)
			{
				App.Logger.LogError(ex, "Error extracting archive file.");
				result = new ArchiveResult(false, false, 0, 0, ex.Message);
			}

			StatusCenterViewModel.RemoveItem(statusCard);

			StatusCenterHelper.AddCard_Decompress(
				archiveFilePath.CreateEnumerable(),
				destinationFolderPath.CreateEnumerable(),
				result.Succeeded ? ReturnResult.Success : result.Cancelled || statusCard.CancellationToken.IsCancellationRequested ? ReturnResult.Cancelled : ReturnResult.Failed);

			if (!result.Succeeded && !result.Cancelled && result.Error is not null)
			{
				App.Logger.LogWarning("Extraction refused or failed: {Error}", result.Error);
				await RunOnUIAsync(async () => { await ShowRefusedAsync(result.Error); return true; });
			}

			return result.Succeeded;
		}

		/// <inheritdoc/>
		public string GenerateArchiveNameFromItems(IReadOnlyList<ListedItem> items)
		{
			if (!items.Any())
				return string.Empty;

			return
				SystemIO.Path.GetFileName(
					items.Count is 1
						? items[0].ItemPath
						: SystemIO.Path.GetDirectoryName(items[0].ItemPath))
					?? string.Empty;
		}

		/// <inheritdoc/>
		public async Task<bool> IsEncryptedAsync(string archiveFilePath)
		{
			try
			{
				return await ArchiveService.IsEncryptedAsync(archiveFilePath);
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Could not inspect the archive.");
				return false;
			}
		}

		/// <inheritdoc/>
		public async Task<bool> HasMultipleTopLevelEntriesAsync(string archiveFilePath, string password = "")
		{
			try
			{
				return await ArchiveService.HasMultipleTopLevelEntriesAsync(archiveFilePath, string.IsNullOrEmpty(password) ? null : password);
			}
			catch (Exception)
			{
				return true;
			}
		}

		/// <inheritdoc/>
		public Task<bool> IsEncodingUndeterminedAsync(string archiveFilePath)
		{
			// LINUX-TODO(archives): legacy (non UTF-8) zip file name encoding detection. SharpCompress picks UTF-8/CP437 itself.
			return Task.FromResult(false);
		}

		/// <inheritdoc/>
		public Task<Encoding?> DetectEncodingAsync(string archiveFilePath)
			=> Task.FromResult<Encoding?>(null);

		private static ArchiveFormat ToPlatformFormat(ArchiveFormats format) => format switch
		{
			ArchiveFormats.SevenZip => ArchiveFormat.SevenZip,
			ArchiveFormats.Tar => ArchiveFormat.Tar,
			ArchiveFormats.TarGz or ArchiveFormats.GZip => ArchiveFormat.TarGz,
			ArchiveFormats.TarBz2 => ArchiveFormat.TarBz2,
			_ => ArchiveFormat.Zip,
		};

		private static ArchiveCompressionLevel ToPlatformLevel(ArchiveCompressionLevels level) => level switch
		{
			ArchiveCompressionLevels.None => ArchiveCompressionLevel.None,
			ArchiveCompressionLevels.Fast or ArchiveCompressionLevels.Low => ArchiveCompressionLevel.Fast,
			ArchiveCompressionLevels.Normal => ArchiveCompressionLevel.Normal,
			ArchiveCompressionLevels.High => ArchiveCompressionLevel.High,
			_ => ArchiveCompressionLevel.Ultra,
		};

		private static Task<T> RunOnUIAsync<T>(Func<Task<T>> action)
		{
			var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
			if (!MainWindow.Instance.DispatcherQueue.TryEnqueue(async () =>
			{
				try
				{
					completion.SetResult(await action());
				}
				catch (Exception ex)
				{
					completion.SetException(ex);
				}
			}))
			{
				completion.SetException(new InvalidOperationException("The UI thread is not available."));
			}

			return completion.Task;
		}

		private static ContentDialog CreateDialog(string title, string content)
		{
			return new ContentDialog
			{
				Title = title,
				Content = new TextBlock { Text = content, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
				XamlRoot = MainWindow.Instance.Content.XamlRoot,
			};
		}

		private static async Task<bool> ConfirmLimitAsync(ArchiveLimitViolation violation)
		{
			var description = violation.Limit switch
			{
				"bytes" => $"{((double)violation.Actual).ToSizeString()} > {((double)violation.Allowed).ToSizeString()}",
				"entries" => $"{violation.Actual:N0} > {violation.Allowed:N0}",
				_ => $"{violation.Actual:N0}:1 > {violation.Allowed:N0}:1",
			};

			var dialog = CreateDialog(Strings.ArchiveLimitExceededTitle.GetLocalizedResource(), string.Format(Strings.ArchiveLimitExceededContent.GetLocalizedResource(), description));
			dialog.PrimaryButtonText = Strings.ArchiveLimitContinue.GetLocalizedResource();
			dialog.CloseButtonText = Strings.Cancel.GetLocalizedResource();
			dialog.DefaultButton = ContentDialogButton.Close;

			return await dialog.TryShowAsync() == ContentDialogResult.Primary;
		}

		private static async Task ShowRefusedAsync(string message)
		{
			var dialog = CreateDialog(Strings.ArchiveRefusedTitle.GetLocalizedResource(), message);
			dialog.CloseButtonText = Strings.Close.GetLocalizedResource();
			await dialog.TryShowAsync();
		}

		private static async Task<ConflictResolution> ResolveConflictAsync(ArchiveConflict conflict)
		{
			var applyToAll = new CheckBox { Content = Strings.ApplyToAllConflictingItems.GetLocalizedResource() };
			var panel = new StackPanel { Spacing = 12 };
			panel.Children.Add(new TextBlock { Text = conflict.EntryPath, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
			panel.Children.Add(applyToAll);

			var dialog = new ContentDialog
			{
				Title = Strings.ItemAlreadyExistsDialogTitle.GetLocalizedResource(),
				Content = panel,
				PrimaryButtonText = Strings.ReplaceExisting.GetLocalizedResource(),
				SecondaryButtonText = Strings.GenerateNewName.GetLocalizedResource(),
				CloseButtonText = Strings.Skip.GetLocalizedResource(),
				DefaultButton = ContentDialogButton.Secondary,
				XamlRoot = MainWindow.Instance.Content.XamlRoot,
			};

			var action = await dialog.TryShowAsync() switch
			{
				ContentDialogResult.Primary => ConflictAction.Overwrite,
				ContentDialogResult.Secondary => ConflictAction.KeepBoth,
				_ => ConflictAction.Skip,
			};

			return new ConflictResolution(action, applyToAll.IsChecked == true);
		}
	}
}
