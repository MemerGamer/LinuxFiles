// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.WinRT;
using Windows.Win32.UI.Shell;

namespace Files.App.ViewModels.UserControls.Widgets
{
	public sealed partial class QuickAccessWidgetViewModel
	{
		private sealed record FolderSnapshot(IAgileReference ShellItem, string Text, string Path, bool IsPinned, string Tooltip);

		public Task RefreshWidgetAsync()
		{
			return MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
			{
				if (isDisposed)
					return;

				var refreshVersion = ++_refreshVersion;
				var homeFolder = HomePageContext.HomeFolder;
				var folders = await STATask.RunPooled(() =>
				{
					List<FolderSnapshot> result = [];
					// The Shell enumerator and its metadata reads stay on this persistent STA.
					foreach (IWindowsStorable folder in homeFolder.GetQuickAccessFolderAsync().ToBlockingEnumerable())
					{
						using (folder)
						{
							folder.GetPropertyValue<bool>("System.Home.IsPinned", out var isPinned);
							folder.TryGetShellTooltip(out var tooltip);
							var text = folder.GetDisplayName(SIGDN.SIGDN_PARENTRELATIVEFORUI);
							var path = folder.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING);
							var hr = PInvoke.RoGetAgileReference(AgileReferenceOptions.AGILEREFERENCE_DEFAULT, typeof(IShellItem).GUID, folder.ThisPtr, out IAgileReference shellItem);
							if (hr.ThrowIfFailedOnDebug().Failed)
								continue;

							result.Add(new(shellItem, text, path, isPinned, tooltip ?? string.Empty));
						}
					}
					return result;
				}, App.Logger);

				if (isDisposed || refreshVersion != _refreshVersion || folders is null)
					return;

				foreach (var item in Items)
					item.Dispose();

				Items.Clear();

				foreach (var folder in folders)
				{
					if (folder.ShellItem.Resolve(out IShellItem shellItem).ThrowIfFailedOnDebug().Failed)
						continue;

					Items.Add(new WidgetFolderCardItem(new WindowsFolder(shellItem), folder.Text, folder.Path, folder.IsPinned, folder.Tooltip));
				}
			});
		}


		public override async Task ExecutePinToSidebarCommand(WidgetCardItem? item)
		{
			if (item is not WidgetFolderCardItem folderCardItem || folderCardItem.Path is null)
				return;



			var lastPinnedItemIndex = Items.LastOrDefault(x => x.IsPinned) is { } lastPinnedItem ? Items.IndexOf(lastPinnedItem) : 0;
			var currentPinnedItemIndex = Items.IndexOf(folderCardItem);

			if (currentPinnedItemIndex is -1)
				return;

			HRESULT hr = PInvoke.RoGetAgileReference(AgileReferenceOptions.AGILEREFERENCE_DEFAULT, typeof(IShellItem).GUID, ((IWindowsStorable)folderCardItem.Item).ThisPtr, out IAgileReference pAgileReference);
			if (hr.ThrowIfFailedOnDebug().Failed)
				return;

			// Pin to Quick Access on Windows
			hr = await STATask.Run(() =>
			{
				hr = pAgileReference.Resolve(out IShellItem pShellItem);
				if (hr.ThrowIfFailedOnDebug().Failed)
					return hr;

				using var windowsFile = new WindowsFile(pShellItem);
				// NOTE: "pintohome" is an undocumented verb, which calls an undocumented COM class, windows.storage.dll!CPinToFrequentExecute : public IExecuteCommand, ...
				return windowsFile.TryInvokeContextMenuVerb("pintohome");
			}, App.Logger);

			// The file watcher will update the collection automatically
		}


		public override async Task ExecuteUnpinFromSidebarCommand(WidgetCardItem? item)
		{
			if (item is not WidgetFolderCardItem folderCardItem || folderCardItem.Path is null)
				return;



			HRESULT hr = PInvoke.RoGetAgileReference(AgileReferenceOptions.AGILEREFERENCE_DEFAULT, typeof(IShellItem).GUID, ((IWindowsStorable)folderCardItem.Item).ThisPtr, out IAgileReference pAgileReference);
			if (hr.ThrowIfFailedOnDebug().Failed)
				return;

			// Unpin from Quick Access on Windows
			hr = await STATask.Run(() =>
			{
				hr = pAgileReference.Resolve(out IShellItem pShellItem);
				if (hr.ThrowIfFailedOnDebug().Failed)
					return hr;

				using var windowsFile = new WindowsFile(pShellItem);

				// NOTE: "unpinfromhome" is an undocumented verb, which calls an undocumented COM class, windows.storage.dll!CRemoveFromFrequentPlacesExecute : public IExecuteCommand, ...
				// NOTE: "remove" is for some shell folders where the "unpinfromhome" may not work
				return windowsFile.TryInvokeContextMenuVerbs(["unpinfromhome", "remove"], true);
			}, App.Logger);

			if (hr.ThrowIfFailedOnDebug().Failed)
				return;

			// The file watcher will update the collection automatically
		}

	}
}
