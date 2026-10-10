// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.System;

namespace Files.App.Data.Models
{
	public sealed class ContextMenuFlyoutItemViewModelBuilder
	{
		private static readonly ContextMenuFlyoutItemViewModel none = new()
		{
			ShowItem = false,
			IsHidden = true,
		};

		private readonly ContextMenuCommandSnapshot command;

		private bool? isVisible = null;
		public bool IsVisible
		{
			get => isVisible ?? command.IsExecutable;
			init => isVisible = value;
		}

		public bool IsPrimary { get; init; } = false;

		public bool IsToggle { get; init; } = false;

		public string AccessKey { get; init; } = string.Empty;

		public object? Tag { get; init; }

		public bool ShowOnShift { get; init; } = false;

		public List<ContextMenuFlyoutItemViewModel>? Items { get; init; } = null;

		public ContextMenuFlyoutItemViewModelBuilder(IRichCommand command)
		{
			this.command = ContextMenuCommandSnapshot.Capture(command);
		}

		internal ContextMenuFlyoutItemViewModelBuilder(ContextMenuCommandSnapshot command)
		{
			this.command = command;
		}

		public ContextMenuFlyoutItemViewModel Build()
		{
			if (isVisible is false)
				return none;

			bool isExecutable = command.IsExecutable;

			if (isVisible is null && !isExecutable)
				return none;

			ContextMenuFlyoutItemType type = IsToggle ? ContextMenuFlyoutItemType.Toggle : ContextMenuFlyoutItemType.Item;

			var viewModel = new ContextMenuFlyoutItemViewModel
			{
				Text = command.Label,
				Tag = Tag,
				Command = command.Command,
				IsEnabled = isExecutable,
				IsChecked = command.IsOn,
				IsPrimary = IsPrimary,
				Items = Items,
				ItemType = type,
				ShowItem = true,
				ShowOnShift = ShowOnShift,
				ShowInRecycleBin = true,
				ShowInSearchPage = true,
				ShowInFtpPage = true,
				ShowInZipPage = true,
				AccessKey = string.IsNullOrWhiteSpace(AccessKey) ? command.AccessKey : AccessKey,
			};

			var glyph = command.Glyph;
			if (!string.IsNullOrEmpty(glyph.ThemedIconStyle))
			{
				viewModel.ThemedIconModel = new ThemedIconModel
				{
					ThemedIconStyle = glyph.ThemedIconStyle,
				};
			}
			else
			{
				viewModel.Glyph = glyph.BaseGlyph;
				viewModel.GlyphFontFamilyName = glyph.FontFamily;
			}

#if WINDOWS
			if (command.Key is { } key)
				viewModel.KeyboardAccelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = key, Modifiers = command.Modifiers };
#else
			viewModel.KeyboardAcceleratorKey = command.Key;
			viewModel.KeyboardAcceleratorModifiers = command.Modifiers;
#endif
			viewModel.KeyboardAcceleratorTextOverride = command.HotKeyLabel;

			return viewModel;
		}
	}
}
