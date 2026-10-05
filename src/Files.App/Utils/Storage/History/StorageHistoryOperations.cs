// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.Diagnostics.CodeAnalysis;
using Windows.Storage;

namespace Files.App.Utils.Storage
{
	public sealed partial class StorageHistoryOperations : IStorageHistoryOperations
	{
		private IFilesystemHelpers? helpers;
		private IFilesystemOperations? operations;

		private readonly CancellationToken cancellationToken;

		public StorageHistoryOperations(IShellPage associatedInstance, CancellationToken cancellationToken)
			: this(associatedInstance.FilesystemHelpers, FilesystemOperationsFactory.Create(associatedInstance), cancellationToken)
		{
		}

		internal StorageHistoryOperations(IFilesystemHelpers helpers, IFilesystemOperations operations, CancellationToken cancellationToken)
		{
			this.cancellationToken = cancellationToken;
			this.helpers = helpers;
			this.operations = operations;
		}

		public async Task<ReturnResult> Undo(IStorageHistory history)
		{
			var helpers = this.helpers ?? throw new ObjectDisposedException(nameof(StorageHistoryOperations));
			var operations = this.operations ?? throw new ObjectDisposedException(nameof(StorageHistoryOperations));
			ReturnResult returnStatus = ReturnResult.InProgress;
#if WINDOWS
			Progress<StatusCenterItemProgressModel> progress = new();

			progress.ProgressChanged += (s, e) => returnStatus = e.Status!.Value.ToStatus();
#else
			HistoryProgress progress = new();
#endif

			switch (history.OperationType)
			{
				case FileOperationType.CreateNew: // Opposite: Delete created items
					if (!IsHistoryNull(history.Source))
					{
						// Show a dialog regardless of the setting to prevent unexpected deletion
						return await helpers.DeleteItemsAsync(history.Source, DeleteConfirmationPolicies.Always, true, false);
					}
					break;
				case FileOperationType.CreateLink: // Opposite: Delete created items
					var createdLinks = history.Destination;
					if (!IsHistoryNull(createdLinks))
					{
						// Show a dialog regardless of the setting to prevent unexpected deletion
						return await helpers.DeleteItemsAsync(createdLinks, DeleteConfirmationPolicies.Always, true, false);
					}
					break;
				case FileOperationType.Rename: // Opposite: Restore original item names
					var renamedItems = history.Destination;
					if (!IsHistoryNull(history.Source) && !IsHistoryNull(renamedItems))
					{
						NameCollisionOption collision = NameCollisionOption.GenerateUniqueName;
						for (int i = 0; i < renamedItems.Count; i++)
						{
							string name = Path.GetFileName(history.Source[i].Path);
#if WINDOWS
							await operations.RenameAsync(renamedItems[i], name, collision, progress, cancellationToken);
#else
							var replayed = await operations.RenameAsync(renamedItems[i], name, collision, progress, cancellationToken);
							if (replayed?.Destination is { Count: > 0 })
								history.Source[i] = replayed.Destination[0];
#endif
						}
					}
					break;
				case FileOperationType.Copy: // Opposite: Delete copied items
					var copiedItems = history.Destination;
					if (!IsHistoryNull(copiedItems))
					{
						// Show a dialog regardless of the setting to prevent unexpected deletion
						return await helpers.DeleteItemsAsync(copiedItems, DeleteConfirmationPolicies.Always, true, false);
					}
					break;
				case FileOperationType.Move: // Opposite: Move the items to original directory
					var movedItems = history.Destination;
					if (!IsHistoryNull(history.Source) && !IsHistoryNull(movedItems))
					{
#if WINDOWS
						return await helpers.MoveItemsAsync(movedItems, history.Source.Select(item => item.Path), false, false);
#else
						return await helpers.MoveItemsAsync(movedItems, history.Source.Select(item => item.Path), false, false, replayed => UpdateHistory(history, replayed, true));
#endif
					}
					break;
				case FileOperationType.Extract: // Opposite: No opposite for archive extraction
					returnStatus = ReturnResult.Success;
					Debugger.Break();
					break;
				case FileOperationType.Recycle: // Opposite: Restore recycled items
					var recycledItems = history.Destination;
					if (!IsHistoryNull(history.Source) && !IsHistoryNull(recycledItems))
					{
#if WINDOWS
						returnStatus = await helpers.RestoreItemsFromTrashAsync(recycledItems, history.Source.Select(item => item.Path), false);
#else
						returnStatus = await helpers.RestoreItemsFromTrashAsync(recycledItems, history.Source.Select(item => item.Path), false, replayed => UpdateHistory(history, replayed, true));
#endif
						if (returnStatus is ReturnResult.IntegrityCheckFailed) // Not found, corrupted
						{
							App.HistoryWrapper.RemoveHistory(history, false);
						}
					}
					break;
				case FileOperationType.Restore: // Opposite: Move restored items to Recycle Bin
					var restoredItems = history.Destination;
					if (!IsHistoryNull(restoredItems))
					{
						var newHistory = await operations.DeleteItemsAsync(restoredItems, progress, false, cancellationToken);
						if (newHistory is null)
						{
							App.HistoryWrapper.RemoveHistory(history, false);
						}
						else
						{
							// We need to change the recycled item paths (since IDs are different) - for Redo() to work
#if WINDOWS
							App.HistoryWrapper.ModifyCurrentHistory(newHistory);
#else
							UpdateHistory(history, newHistory, true);
#endif
						}
					}
					break;
				case FileOperationType.Delete: // Opposite: No opposite for pernament deletion
					returnStatus = ReturnResult.Success;
					break;
			}

#if WINDOWS
			return returnStatus;
#else
			return progress.Status ?? returnStatus;
#endif
		}

		public async Task<ReturnResult> Redo(IStorageHistory history)
		{
			var helpers = this.helpers ?? throw new ObjectDisposedException(nameof(StorageHistoryOperations));
			var operations = this.operations ?? throw new ObjectDisposedException(nameof(StorageHistoryOperations));
			ReturnResult returnStatus = ReturnResult.InProgress;
#if WINDOWS
			Progress<StatusCenterItemProgressModel> progress = new();

			progress.ProgressChanged += (s, e) => { returnStatus = e.Status!.Value.ToStatus(); };
#else
			HistoryProgress progress = new();
#endif

			switch (history.OperationType)
			{
				case FileOperationType.CreateNew:
					break;
				case FileOperationType.CreateLink:
					var linkDestinations = history.Destination;
					if (!IsHistoryNull(history.Source) && !IsHistoryNull(linkDestinations))
					{
						await operations.CreateShortcutItemsAsync(history.Source,
							await linkDestinations.Select(item => item.Path).ToListAsync(), progress, cancellationToken);
					}
					break;
				case FileOperationType.Rename:
					var renameDestinations = history.Destination;
					if (!IsHistoryNull(history.Source) && !IsHistoryNull(renameDestinations))
					{
						NameCollisionOption collision = NameCollisionOption.GenerateUniqueName;
						for (int i = 0; i < history.Source.Count; i++)
						{
							string name = Path.GetFileName(renameDestinations[i].Path);
#if WINDOWS
							await operations.RenameAsync(history.Source[i], name, collision, progress, cancellationToken);
#else
							var replayed = await operations.RenameAsync(history.Source[i], name, collision, progress, cancellationToken);
							if (replayed?.Destination is { Count: > 0 })
								renameDestinations[i] = replayed.Destination[0];
#endif
						}
					}
					break;
				case FileOperationType.Copy:
					var copyDestinations = history.Destination;
					if (!IsHistoryNull(history.Source) && !IsHistoryNull(copyDestinations))
					{
#if WINDOWS
						return await helpers.CopyItemsAsync(history.Source, copyDestinations.Select(item => item.Path), false, false);
#else
						return await helpers.CopyItemsAsync(history.Source, copyDestinations.Select(item => item.Path), false, false, replayed => UpdateHistory(history, replayed, false));
#endif
					}
					break;
				case FileOperationType.Move:
					var moveDestinations = history.Destination;
					if (!IsHistoryNull(history.Source) && !IsHistoryNull(moveDestinations))
					{
#if WINDOWS
						return await helpers.MoveItemsAsync(history.Source, moveDestinations.Select(item => item.Path), false, false);
#else
						return await helpers.MoveItemsAsync(history.Source, moveDestinations.Select(item => item.Path), false, false, replayed => UpdateHistory(history, replayed, false));
#endif
					}
					break;
				case FileOperationType.Extract:
					returnStatus = ReturnResult.Success;
					Debugger.Break();
					break;
				case FileOperationType.Recycle: // Recycle PASS
					var recycleDestinations = history.Destination;
					if (!IsHistoryNull(history.Source) && !IsHistoryNull(recycleDestinations))
					{
						var newHistory = await operations.DeleteItemsAsync(history.Source, progress, false, cancellationToken);
						if (newHistory is null)
						{
							App.HistoryWrapper.RemoveHistory(history, true);
						}
						else
						{
							// We need to change the recycled item paths (since IDs are different) - for Undo() to work
							App.HistoryWrapper.ModifyCurrentHistory(newHistory);
						}
					}
					break;
				case FileOperationType.Restore:
					var restoreDestinations = history.Destination;
					if (!IsHistoryNull(history.Source) && !IsHistoryNull(restoreDestinations))
					{
#if WINDOWS
						await helpers.RestoreItemsFromTrashAsync(history.Source, restoreDestinations.Select(item => item.Path), false);
#else
						return await helpers.RestoreItemsFromTrashAsync(history.Source, restoreDestinations.Select(item => item.Path), false, replayed => UpdateHistory(history, replayed, false));
#endif
					}
					break;
				case FileOperationType.Delete:
					returnStatus = ReturnResult.Success;
					break;
			}

#if WINDOWS
			return returnStatus;
#else
			return progress.Status ?? returnStatus;
#endif
		}

		public void Dispose()
		{
			helpers?.Dispose();
			helpers = null;

			operations?.Dispose();
			operations = null;
		}

#if !WINDOWS
		private static void UpdateHistory(IStorageHistory history, IStorageHistory? replayed, bool reverse)
		{
			if (replayed?.Destination is not null)
				history.Modify(new StorageHistory(history.OperationType,
					reverse ? replayed.Destination : replayed.Source,
					reverse ? replayed.Source : replayed.Destination));
		}

		private sealed class HistoryProgress : IProgress<StatusCenterItemProgressModel>
		{
			private StatusCenterItemProgressModel? model;

			// The model can reach its terminal status while its last notification is throttled.
			public ReturnResult? Status => model?.Status?.ToStatus();

			public void Report(StatusCenterItemProgressModel value) => model = value;
		}
#endif

		private static bool IsHistoryNull([NotNullWhen(false)] IEnumerable<IStorageItemWithPath>? source)
			=> source is null || !source.All(HasPath);

		private static bool HasPath(IStorageItemWithPath item) => item is not null && !string.IsNullOrWhiteSpace(item.Path);
	}
}
