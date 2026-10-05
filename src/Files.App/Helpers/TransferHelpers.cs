// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.DataTransfer;
using Files.App.Services.Desktop;

namespace Files.App.Helpers
{
	public static partial class TransferHelpers
	{
		public static async Task ExecuteTransferAsync(IReadOnlyList<IStorable> itemsToTransfer, ShellViewModel shellViewModel, StatusCenterViewModel statusViewModel, DataPackageOperation type = DataPackageOperation.Copy)
		{
			var paths = itemsToTransfer.Select(x => x.Id).ToArray();
			FileClipboard.Set(paths, type);
			await PublishToSystemClipboardAsync(paths, type);
		}

		public static async Task ExecuteTransferAsync(IContentPageContext context, StatusCenterViewModel statusViewModel, DataPackageOperation type = DataPackageOperation.Copy)
		{
			if (context.ShellPage?.SlimContentPage is not { } contentPage || !contentPage.IsItemSelected)
				return;

			contentPage.ItemManipulationModel.RefreshItemsOpacity();
			var selected = context.SelectedItems.ToList();
			var paths = selected.Select(x => x.ItemPath).ToArray();
			FileClipboard.Set(paths, type);
			await PublishToSystemClipboardAsync(paths, type);
			if (type is DataPackageOperation.Move)
			{
				await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
				{
					foreach (var item in selected)
						item.Opacity = Constants.UI.DimItemOpacity;
				});
			}
		}

		private static async Task PublishToSystemClipboardAsync(string[] paths, DataPackageOperation type)
		{
			try
			{
				await DesktopFileDragHelper.PublishFilesAsync(paths, type);
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Failed to publish the copied files to the system clipboard");
			}
		}
	}
}
#endif
