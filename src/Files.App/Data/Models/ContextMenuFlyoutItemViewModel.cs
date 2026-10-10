// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Windows.Input;
using Windows.System;

namespace Files.App.Data.Models
{
	public sealed class ContextMenuFlyoutItemViewModel
	{
		public bool ShowItem { get; set; } = true;

		public ICommand? Command { get; set; }

		public object? CommandParameter { get; set; }

		public string? Glyph { get; set; }

		public string? GlyphFontFamilyName { get; set; }

		public string? KeyboardAcceleratorTextOverride { get; set; }

		public string? Text { get; set; }

		public object? Tag { get; set; }

		public ContextMenuFlyoutItemType ItemType { get; set; }

		public Func<Task>? LoadSubMenuAction { get; set; }

		public List<ContextMenuFlyoutItemViewModel>? Items { get; set; }

		public BitmapImage? BitmapIcon { get; set; }

		public bool ShowOnShift { get; set; }

		public bool SingleItemOnly { get; set; }

		public bool ShowInRecycleBin { get; set; }

		public bool ShowInSearchPage { get; set; }

		public bool ShowInFtpPage { get; set; }

		public bool ShowInZipPage { get; set; }

		public KeyboardAccelerator? KeyboardAccelerator { get; set; }

#if !WINDOWS
		public VirtualKey? KeyboardAcceleratorKey { get; set; }

		public VirtualKeyModifiers KeyboardAcceleratorModifiers { get; set; }
#endif

		internal KeyboardAccelerator? CreateKeyboardAccelerator()
		{
			if (KeyboardAccelerator is { } accelerator)
				return new() { Key = accelerator.Key, Modifiers = accelerator.Modifiers };
#if !WINDOWS
			if (KeyboardAcceleratorKey is { } key)
				return new() { Key = key, Modifiers = KeyboardAcceleratorModifiers };
#endif
			return null;
		}

		public bool IsChecked { get; set; }

		public bool IsEnabled { get; set; } = true;

		public string? ID { get; set; }

		public bool IsPrimary { get; set; }

		public bool CollapseLabel { get; set; }

		public ThemedIconModel ThemedIconModel { get; set; }

		public bool ShowLoadingIndicator { get; set; }

		public bool IsHidden { get; set; }

		public string AccessKey { get; set; } = string.Empty;
	}
}
