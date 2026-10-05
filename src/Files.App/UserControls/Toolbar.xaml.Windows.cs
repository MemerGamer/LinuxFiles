// Copyright (c) Files Community
// Licensed under the MIT License.

using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using FlyoutPlacementMode = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode;

namespace Files.App.UserControls
{
	public sealed partial class Toolbar : UserControl
	{
		private OpenWithMenu? openWithMenu;

		private async Task PopulateOpenWithFlyoutAsync(MenuFlyout flyout)
		{
			var requestId = ++openWithFlyoutRequestId;

			flyout.Items.Add(new MenuFlyoutItem
			{
				Text = Strings.Loading.GetLocalizedResource(),
				IsEnabled = false,
			});

			openWithMenu?.Dispose();
			openWithMenu = null;

			OpenWithMenu? loadedOpenWithMenu = null;
			if (PageContext.SelectedItems.Count is 1 && PageContext.SelectedItem?.ItemPath is string path)
				loadedOpenWithMenu = await OpenWithMenu.GetForFileAsync(path);

			if (requestId != openWithFlyoutRequestId)
			{
				loadedOpenWithMenu?.Dispose();
				return;
			}

			openWithMenu = loadedOpenWithMenu;

			flyout.Items.Clear();

			if (openWithMenu is not null)
			{
				foreach (var item in openWithMenu.Items.Where(x => x.Type is MENU_ITEM_TYPE.MFT_STRING && !string.IsNullOrWhiteSpace(x.Label)))
					flyout.Items.Add(await CreateOpenWithMenuItemAsync(openWithMenu, item));
			}

			if (flyout.Items.Count == 0)
				flyout.Items.Add(CreateChooseAnotherAppMenuItem());
		}

		private static async Task<MenuFlyoutItem> CreateOpenWithMenuItemAsync(OpenWithMenu menu, Win32ContextMenuItem entry)
		{
			MenuFlyoutItem item;
			if (entry.Icon is { Length: > 0 })
			{
				using var ms = new MemoryStream(entry.Icon);
				var image = new BitmapImage();
				await image.SetSourceAsync(ms.AsRandomAccessStream());
				item = new MenuFlyoutItemWithImage { BitmapIcon = image };
			}
			else
			{
				item = new MenuFlyoutItem();
			}

			item.Text = entry.Label;
			item.Command = new AsyncRelayCommand(async () => await menu.InvokeItem(entry.ID));

			return item;
		}
	}
}
