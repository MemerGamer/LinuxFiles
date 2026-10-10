// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.System;

namespace Files.App.Data.Models
{
	internal sealed record ContextMenuCommandSnapshot(
		IRichCommand Command, string Label, RichGlyph Glyph, string AccessKey,
		bool IsExecutable, bool IsOn, VirtualKey? Key, VirtualKeyModifiers Modifiers, string? HotKeyLabel)
	{
		public static ContextMenuCommandSnapshot Capture(IRichCommand command)
		{
			var hotKey = command.HotKeys.FirstOrDefault();
			var hasAccelerator = command.HotKeys.Length > 0 && !(hotKey.Key is Keys.Enter && hotKey.Modifier is KeyModifiers.None);
			return new(command, command.Label, command.Glyph, command.AccessKey, command.IsExecutable, command.IsOn,
				hasAccelerator ? (VirtualKey)hotKey.Key : null,
				hasAccelerator ? (VirtualKeyModifiers)hotKey.Modifier : VirtualKeyModifiers.None,
				hasAccelerator ? hotKey.LocalizedLabel : null);
		}
	}
}
