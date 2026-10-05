// Copyright (c) Files Community
// Licensed under the MIT License.

using CommunityToolkit.WinUI;
using Files.App.Helpers.ContextFlyouts;
using Files.Shared.Helpers;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
using System.Text;
using Windows.System;
using Windows.UI.Core;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.App.Helpers
{
	public static partial class ShellContextFlyoutFactory
	{
		private static (string Label, string AccessKey) ExtractLabelAndAccessKey(string rawLabel)
		{
			if (string.IsNullOrEmpty(rawLabel))
				return (string.Empty, string.Empty);

			string accessKey = string.Empty;
			var labelBuilder = new StringBuilder(rawLabel.Length);

			for (int i = 0; i < rawLabel.Length; i++)
			{
				char current = rawLabel[i];
				if (current != '&')
				{
					labelBuilder.Append(current);
					continue;
				}

				if (i + 1 >= rawLabel.Length)
				{
					labelBuilder.Append('&');
					continue;
				}

				char next = rawLabel[++i];
				if (next == '&')
				{
					labelBuilder.Append('&');
					continue;
				}

				if (string.IsNullOrEmpty(accessKey) && !char.IsWhiteSpace(next))
					accessKey = char.ToUpperInvariant(next).ToString();

				labelBuilder.Append(next);
			}

			return (labelBuilder.ToString(), accessKey);
		}

		private static ContextMenuFlyoutItemViewModel CreateShellMenuItem(Win32ContextMenuItem menuFlyoutItem, BitmapImage? image)
		{
			var (label, accessKey) = ExtractLabelAndAccessKey(menuFlyoutItem.Label ?? string.Empty);
			return new ContextMenuFlyoutItemViewModel
			{
				Text = label,
				AccessKey = accessKey,
				Tag = menuFlyoutItem,
				BitmapIcon = image,
			};
		}

		private static async Task LoadMenuFlyoutItemAsync(
			List<ContextMenuFlyoutItemViewModel> menuItemsListLocal,
			ContextMenu contextMenu,
			IEnumerable<Win32ContextMenuItem> menuFlyoutItems,
			CancellationToken cancellationToken,
			bool showIcons = true,
			int itemsBeforeOverflow = int.MaxValue)
		{
			if (cancellationToken.IsCancellationRequested)
				return;

			var itemsCount = 0; // Separators do not count for reaching the overflow threshold
			var menuItems = menuFlyoutItems.TakeWhile(x => x.Type == MENU_ITEM_TYPE.MFT_SEPARATOR || ++itemsCount <= itemsBeforeOverflow).ToList();
			var overflowItems = menuFlyoutItems.Except(menuItems).ToList();

			if (overflowItems.Any(x => x.Type != MENU_ITEM_TYPE.MFT_SEPARATOR))
			{
				var moreItem = menuItemsListLocal.FirstOrDefault(x => x.ID == "ItemOverflow");
				if (moreItem is null)
				{
					var menuLayoutSubItem = new ContextMenuFlyoutItemViewModel()
					{
						Text = Strings.ShowMoreOptions.GetLocalizedResource(),
						Glyph = "\xE712",
					};
					await LoadMenuFlyoutItemAsync(menuLayoutSubItem.Items
						?? throw new InvalidOperationException("The shell overflow menu has not been initialized."), contextMenu, overflowItems, cancellationToken, showIcons);
					menuItemsListLocal.Insert(0, menuLayoutSubItem);
				}
				else
				{
					await LoadMenuFlyoutItemAsync(moreItem.Items
						?? throw new InvalidOperationException("The shell overflow menu has not been initialized."), contextMenu, overflowItems, cancellationToken, showIcons);
				}
			}

			foreach (var menuFlyoutItem in menuItems
				.SkipWhile(x => x.Type == MENU_ITEM_TYPE.MFT_SEPARATOR) // Remove leading separators
				.Reverse()
				.SkipWhile(x => x.Type == MENU_ITEM_TYPE.MFT_SEPARATOR)) // Remove trailing separators
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				// Avoid duplicate separators
				if ((menuFlyoutItem.Type == MENU_ITEM_TYPE.MFT_SEPARATOR) && (menuItemsListLocal.FirstOrDefault()?.ItemType == ContextMenuFlyoutItemType.Separator))
					continue;

				BitmapImage? image = null;
				if (showIcons && menuFlyoutItem.Icon is { Length: > 0 })
				{
					image = new BitmapImage();
					using var ms = new MemoryStream(menuFlyoutItem.Icon);
					await image.SetSourceAsync(ms.AsRandomAccessStream());
				}

				if (menuFlyoutItem.Type is MENU_ITEM_TYPE.MFT_SEPARATOR)
				{
					var menuLayoutItem = new ContextMenuFlyoutItemViewModel()
					{
						ItemType = ContextMenuFlyoutItemType.Separator,
						Tag = menuFlyoutItem
					};
					menuItemsListLocal.Insert(0, menuLayoutItem);
				}
				else if (!string.IsNullOrEmpty(menuFlyoutItem.Label) && menuFlyoutItem.SubItems is not null)
				{
					if (string.Equals(menuFlyoutItem.Label, Win32Helper.ExtractStringFromDLL("shell32.dll", 30312)))
						menuFlyoutItem.CommandString = "sendto";

					var menuLayoutSubItem = CreateShellMenuItem(menuFlyoutItem, image);
					menuLayoutSubItem.Items = [];

					if (menuFlyoutItem.SubItems.Any())
					{
						await LoadMenuFlyoutItemAsync(menuLayoutSubItem.Items, contextMenu, menuFlyoutItem.SubItems, cancellationToken, showIcons);
					}
					else
					{
						menuLayoutSubItem.LoadSubMenuAction = async () =>
						{
							if (await contextMenu.LoadSubMenu(menuFlyoutItem.SubItems))
								await LoadMenuFlyoutItemAsync(menuLayoutSubItem.Items, contextMenu, menuFlyoutItem.SubItems, cancellationToken, showIcons);
						};
					}

					menuItemsListLocal.Insert(0, menuLayoutSubItem);
				}
				else if (!string.IsNullOrEmpty(menuFlyoutItem.Label))
				{
					var menuLayoutItem = CreateShellMenuItem(menuFlyoutItem, image);
					menuLayoutItem.Command = new AsyncRelayCommand<object>(x => InvokeShellMenuItemAsync(contextMenu, x));
					menuLayoutItem.CommandParameter = menuFlyoutItem;
					menuItemsListLocal.Insert(0, menuLayoutItem);
				}
			}

			async Task InvokeShellMenuItemAsync(ContextMenu contextMenu, object? tag)
			{
				if (tag is not Win32ContextMenuItem menuItem)
					return;

				var menuId = menuItem.ID;
				var isFont = FileExtensionHelpers.IsFontFile(contextMenu.ItemsPath[0]);
				var verb = menuItem.CommandString;
				switch (verb)
				{
					case "install" when isFont:
						await Win32Helper.InstallFontsAsync([.. contextMenu.ItemsPath], false);
						break;

					case "installAllUsers" when isFont:
						await Win32Helper.InstallFontsAsync([.. contextMenu.ItemsPath], true);
						break;

					case "mount":
						var vhdPath = contextMenu.ItemsPath[0];
						await Win32Helper.MountVhdDisk(vhdPath);
						break;

					case "format":
						var drivePath = contextMenu.ItemsPath[0];
						await Win32Helper.OpenFormatDriveDialog(drivePath);
						break;

					case "Windows.PowerShell.Run":
						await contextMenu.InvokeItem(
							menuId,
							contextMenu.ItemsPath[0].EndsWith(".ps1") ? Path.GetDirectoryName(contextMenu.ItemsPath[0]) : null
						);
						break;

					default:
						await contextMenu.InvokeItem(menuId);
						break;
				}

				//contextMenu.Dispose(); // Prevents some menu items from working (TBC)
			}
		}

		private static async Task<List<ContextMenuFlyoutItemViewModel>> GetWindowsContextMenuAsync(bool showOpenMenu, bool shiftPressed, string? workingDirectory, List<ListedItem>? selectedItems, CancellationToken cancellationToken)
		{
			var menuItemsList = new List<ContextMenuFlyoutItemViewModel>();
			var filePaths = selectedItems is { Count: > 0 }
				? selectedItems.Select(x => x.ItemPath!).ToArray()
				: [workingDirectory ?? throw new ArgumentException("A working directory is required when no items are selected.", nameof(workingDirectory))];

			Func<string?, bool> FilterMenuItems(bool showOpenMenu)
			{
				var knownItems = new HashSet<string>()
				{
					"opennew", "opencontaining", "opennewprocess",
					"runas", "runasuser", "pintohome", "PinToStartScreen",
					"cut", "copy", "paste", "delete", "properties", "link",
					"Windows.ModernShare", "setdesktopwallpaper",
					"eject", "rename", "explore", "openinfiles", "extract",
					"copyaspath", "undelete", "empty", "format", "rotate90", "rotate270",
					Win32Helper.ExtractStringFromDLL("shell32.dll", 34593), // Add to collection
					Win32Helper.ExtractStringFromDLL("shell32.dll", 5384), // Pin to Start
					Win32Helper.ExtractStringFromDLL("shell32.dll", 5385), // Unpin from Start
					Win32Helper.ExtractStringFromDLL("shell32.dll", 5386), // Pin to taskbar
					Win32Helper.ExtractStringFromDLL("shell32.dll", 5387), // Unpin from taskbar
					"{9F156763-7844-4DC4-B2B1-901F640F5155}", // Open in Terminal
				};

				bool filterMenuItemsImpl(string? menuItem) => !string.IsNullOrEmpty(menuItem)
					&& (knownItems.Contains(menuItem) || (!showOpenMenu && menuItem.Equals("open", StringComparison.OrdinalIgnoreCase)));

				return filterMenuItemsImpl;
			}

			var contextMenu = await ContextMenu.GetContextMenuForFiles(filePaths,
				shiftPressed ? PInvoke.CMF_EXTENDEDVERBS : PInvoke.CMF_NORMAL, FilterMenuItems(showOpenMenu));

			if (contextMenu is not null)
				await LoadMenuFlyoutItemAsync(menuItemsList, contextMenu, contextMenu.Items!, cancellationToken, true);

			if (cancellationToken.IsCancellationRequested)
				menuItemsList.Clear();

			return menuItemsList;
		}
	}
}
