// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Specialized;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;

namespace Files.App.ViewModels.UserControls.Widgets
{
	/// <summary>
	/// Represents view model of <see cref="QuickAccessWidget"/>.
	/// </summary>
	public sealed partial class QuickAccessWidgetViewModel : BaseWidgetViewModel, IWidgetViewModel
	{
		// Properties

		public ObservableCollection<WidgetFolderCardItem> Items { get; } = [];

		public string WidgetName => nameof(QuickAccessWidget);
		public string AutomationProperties => Strings.QuickAccess.GetLocalizedResource();
		public string WidgetHeader => Strings.QuickAccess.GetLocalizedResource();
		public bool IsWidgetSettingEnabled => UserSettingsService.GeneralSettingsService.ShowQuickAccessWidget;
		public bool ShowMenuFlyout => false;
		public MenuFlyoutItem? MenuFlyoutItem => null;

		// Fields

		// TODO: Use a storable watcher when available.
		private readonly SystemIO.FileSystemWatcher? _quickAccessFolderWatcher;
		private bool isDisposed;
		private int _refreshVersion;



		// Constructor

		public QuickAccessWidgetViewModel()
		{
			Items.CollectionChanged += Items_CollectionChanged;

			OpenPropertiesCommand = new RelayCommand<WidgetFolderCardItem>(ExecuteOpenPropertiesCommand);
			PinToSidebarCommand = new AsyncRelayCommand<WidgetFolderCardItem>(ExecutePinToSidebarCommand);
			UnpinFromSidebarCommand = new AsyncRelayCommand<WidgetFolderCardItem>(ExecuteUnpinFromSidebarCommand);

#if !WINDOWS
			App.QuickAccessManager.UpdateQuickAccessWidget += QuickAccessChanged;
#endif

#if WINDOWS
			var automaticDestinationsPath = SystemIO.Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent", "AutomaticDestinations");
			if (!SystemIO.Directory.Exists(automaticDestinationsPath))
				return;

			_quickAccessFolderWatcher = new()
			{
				Path = automaticDestinationsPath,
				Filter = "f01b4d95cf55d32a.automaticDestinations-ms",
				NotifyFilter = SystemIO.NotifyFilters.LastAccess | SystemIO.NotifyFilters.LastWrite | SystemIO.NotifyFilters.FileName
			};

			_quickAccessFolderWatcher.Changed += QuickAccessFolderWatcher_Changed;

			_quickAccessFolderWatcher.EnableRaisingEvents = true;
#endif
		}

		// Methods

		private async void QuickAccessFolderWatcher_Changed(object sender, SystemIO.FileSystemEventArgs e)
		{
			if (!isDisposed)
				await RefreshWidgetAsync();
		}

#if !WINDOWS
		private async void QuickAccessChanged(object? sender, ModifyQuickAccessEventArgs e)
		{
			if (!isDisposed)
				await RefreshWidgetAsync();
		}

		public Task RefreshWidgetAsync()
		{
			return MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
			{
				if (isDisposed)
					return;
				var refreshVersion = ++_refreshVersion;
				var folders = await QuickAccessService.GetPinnedFoldersAsync();
				if (isDisposed || refreshVersion != _refreshVersion)
					return;

				foreach (var item in Items)
					item.Dispose();
				Items.Clear();

				foreach (var folder in folders)
				{
					if (string.IsNullOrEmpty(folder.FilePath))
						continue;

					Items.Add(new WidgetFolderCardItem(folder.FilePath, folder.FileName ?? folder.FilePath, true, folder.FilePath));
				}
			});
		}

#endif

		public override List<ContextMenuFlyoutItemViewModel> GetItemMenuItems(WidgetCardItem item, bool isPinned, bool isFolder = false)
		{
			return new List<ContextMenuFlyoutItemViewModel>()
			{
				new ContextMenuFlyoutItemViewModelBuilder(CommandManager.OpenInNewTabFromHome)
				{
					IsVisible = UserSettingsService.GeneralSettingsService.ShowOpenInNewTab && CommandManager.OpenInNewTabFromHome.IsExecutable
				}.Build(),
				new ContextMenuFlyoutItemViewModelBuilder(CommandManager.OpenInNewWindowFromHome)
				{
					IsVisible = UserSettingsService.GeneralSettingsService.ShowOpenInNewWindow && CommandManager.OpenInNewWindowFromHome.IsExecutable
				}.Build(),
				new ContextMenuFlyoutItemViewModel()
				{
					Text = Strings.OpenInNewPane.GetLocalizedResource(),
					ShowItem = UserSettingsService.GeneralSettingsService.ShowOpenInNewPane && CommandManager.OpenInNewPaneFromHome.IsExecutable,
					IsEnabled = CommandManager.OpenInNewPaneFromHome.IsExecutable,
					Items =
					[
						new ContextMenuFlyoutItemViewModel()
						{
							Text = Strings.SplitPaneVertically.GetLocalizedResource(),
							ThemedIconModel = new() { ThemedIconStyle = "App.ThemedIcons.OpenInPaneVertical" },
							Command = CommandManager.OpenInNewPaneFromHome,
							CommandParameter = ShellPaneArrangement.Vertical,
						},
						new ContextMenuFlyoutItemViewModel()
						{
							Text = Strings.SplitPaneHorizontally.GetLocalizedResource(),
							ThemedIconModel = new() { ThemedIconStyle = "App.ThemedIcons.OpenInPaneHorizontal" },
							Command = CommandManager.OpenInNewPaneFromHome,
							CommandParameter = ShellPaneArrangement.Horizontal,
						},
					]
				},
				new ContextMenuFlyoutItemViewModelBuilder(CommandManager.OpenInOtherPaneFromHome)
				{
					IsVisible = UserSettingsService.GeneralSettingsService.ShowOpenInNewPane && CommandManager.OpenInOtherPaneFromHome.IsExecutable
				}.Build(),
				new ContextMenuFlyoutItemViewModelBuilder(CommandManager.CopyItemFromHome)
				{
					IsPrimary = true,
					IsVisible = CommandManager.CopyItemFromHome.IsExecutable
				}.Build(),
				new()
				{
					Text = Strings.Properties.GetLocalizedResource(),
					ThemedIconModel = new() { ThemedIconStyle = "App.ThemedIcons.Properties" },
					Command = OpenPropertiesCommand,
					CommandParameter = item,
					IsPrimary = true
				},
				new()
				{
					Text = Strings.PinFolderToSidebar.GetLocalizedResource(),
					ThemedIconModel = new() { ThemedIconStyle = "App.ThemedIcons.FavoritePin" },
					Command = PinToSidebarCommand,
					CommandParameter = item,
					ShowItem = !isPinned && UserSettingsService.GeneralSettingsService.ShowPinToSideBar
				},
				new()
				{
					Text = Strings.UnpinFolderFromSidebar.GetLocalizedResource(),
					ThemedIconModel = new() { ThemedIconStyle = "App.ThemedIcons.FavoritePinRemove" },
					Command = UnpinFromSidebarCommand,
					CommandParameter = item,
					ShowItem = isPinned && UserSettingsService.GeneralSettingsService.ShowPinToSideBar
				},
				new()
				{
					Text = Strings.SendTo.GetLocalizedResource(),
					Tag = "SendToPlaceholder",
					ShowItem = UserSettingsService.GeneralSettingsService.ShowSendToMenu
				},
				new ContextMenuFlyoutItemViewModel()
				{
					ItemType = ContextMenuFlyoutItemType.Separator,
					ShowItem = UserSettingsService.GeneralSettingsService.ShowOpenTerminal && CommandManager.OpenTerminalFromHome.IsExecutable
				},
				new ContextMenuFlyoutItemViewModelBuilder(CommandManager.OpenTerminalFromHome)
				{
					IsVisible = UserSettingsService.GeneralSettingsService.ShowOpenTerminal && CommandManager.OpenTerminalFromHome.IsExecutable
				}.Build(),
				new()
				{
					ItemType = ContextMenuFlyoutItemType.Separator,
					Tag = "OverflowSeparator",
				},
				new()
				{
					Text = Strings.Loading.GetLocalizedResource(),
					Glyph = "\xE712",
					Items = [],
					ID = "ItemOverflow",
					Tag = "ItemOverflow",
					IsEnabled = false,
				}
			}.Where(x => x.ShowItem).ToList();
		}

		public async Task NavigateToPath(string path)
		{
			var ctrlPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
			if (ctrlPressed)
			{
				await NavigationHelpers.OpenPathInNewTab(path);
				return;
			}

			ContentPageContext.ShellPage?.NavigateWithArguments(
				ContentPageContext.ShellPage.InstanceViewModel.FolderSettings.GetLayoutType(path),
				new() { NavPathParam = path });
		}

		// Event methods

		private async void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.Action is NotifyCollectionChangedAction.Add)
			{
				foreach (WidgetFolderCardItem cardItem in e.NewItems!)
					await cardItem.LoadCardThumbnailAsync();
			}
		}

		// Command methods

#if !WINDOWS
		public override async Task ExecutePinToSidebarCommand(WidgetCardItem? item)
		{
			if (item is not WidgetFolderCardItem folderCardItem || folderCardItem.Path is null)
				return;

			await QuickAccessService.PinToSidebarAsync(folderCardItem.Path);
		}
#endif

#if !WINDOWS
		public override async Task ExecuteUnpinFromSidebarCommand(WidgetCardItem? item)
		{
			if (item is not WidgetFolderCardItem folderCardItem || folderCardItem.Path is null)
				return;

			await QuickAccessService.UnpinFromSidebarAsync(folderCardItem.Path);
		}
#endif

		private void ExecuteOpenPropertiesCommand(WidgetFolderCardItem? item)
		{
			if (!HomePageContext.IsAnyItemRightClicked || item?.Item is null)
				return;

			var flyout = HomePageContext.ItemContextFlyoutMenu
				?? throw new InvalidOperationException("The quick-access item context menu is not available.");

			async void FlyoutClosed(object? sender, object args)
			{
				flyout.Closed -= FlyoutClosed;
				var itemPath = item.Path
					?? throw new InvalidOperationException("The quick-access item does not have a path.");
				var shellPage = ContentPageContext.ShellPage
					?? throw new InvalidOperationException("There is no active shell page for quick-access properties.");
				var shellViewModel = shellPage.GetRequiredShellViewModel();

				ListedItem listedItem = new(null)
				{
					ItemPath = itemPath,
					ItemNameRaw = item.Text,
					PrimaryItemAttribute = StorageItemTypes.Folder,
					ItemType = Strings.Folder.GetLocalizedResource(),
				};

#if WINDOWS
				if (!string.Equals(itemPath, Constants.UserEnvironmentPaths.RecycleBinPath, StringComparison.OrdinalIgnoreCase))
				{
					BaseStorageFolder? matchingStorageFolder = (await shellViewModel.GetFolderFromPathAsync(itemPath)).Result;
					if (matchingStorageFolder is not null)
					{
						var syncStatus = await shellViewModel.CheckCloudDriveSyncStatusAsync(matchingStorageFolder);
						listedItem.SyncStatusUI = CloudDriveSyncStatusUI.FromCloudDriveSyncStatus(syncStatus);
					}
				}
#endif

				FilePropertiesHelpers.OpenPropertiesWindow(listedItem, shellPage);
			}

			flyout.Closed += FlyoutClosed;
		}

		// Disposer

		public void Dispose()
		{
			if (isDisposed)
				return;

			isDisposed = true;
			Items.CollectionChanged -= Items_CollectionChanged;
#if !WINDOWS
			App.QuickAccessManager.UpdateQuickAccessWidget -= QuickAccessChanged;
#endif
			if (_quickAccessFolderWatcher is not null)
			{
				_quickAccessFolderWatcher.EnableRaisingEvents = false;
				_quickAccessFolderWatcher.Changed -= QuickAccessFolderWatcher_Changed;
				_quickAccessFolderWatcher.Dispose();
			}

			foreach (var item in Items)
				item.Dispose();

			Items.Clear();
		}
	}
}
