// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Appearance;
using System;

namespace Files.Platform.Linux.Windowing
{
	public enum OpacitySupport { Unknown, Supported, NoCompositor, Satellite }

	public sealed record X11AppearanceSupport(OpacitySupport WindowOpacity, bool BackgroundAlpha, bool Blur)
	{
		public static X11AppearanceSupport Current { get; internal set; } = new(OpacitySupport.Unknown, false, false);

		// A property write or an interned atom alone does not prove compositor support.
		public static X11AppearanceSupport Detect(string? manager, string? desktop, bool compositor, bool blurAdvertised)
		{
			if ((manager?.Contains("satellite", StringComparison.OrdinalIgnoreCase) ?? false) ||
				(desktop?.Contains("niri", StringComparison.OrdinalIgnoreCase) ?? false))
				return new(OpacitySupport.Satellite, false, false);
			if (!compositor)
				return new(OpacitySupport.NoCompositor, false, false);
			var known = manager is not null && (manager.Contains("KWin", StringComparison.OrdinalIgnoreCase) ||
				manager.Contains("Mutter", StringComparison.OrdinalIgnoreCase) || manager.Contains("GNOME Shell", StringComparison.OrdinalIgnoreCase) ||
				manager.Contains("Xfwm", StringComparison.OrdinalIgnoreCase) || manager.Contains("picom", StringComparison.OrdinalIgnoreCase) ||
				manager.Contains("compiz", StringComparison.OrdinalIgnoreCase));
			return new(known ? OpacitySupport.Supported : OpacitySupport.Unknown, known,
				known && manager!.Contains("KWin", StringComparison.OrdinalIgnoreCase) && blurAdvertised);
		}

		public BackdropMode Resolve(BackdropMode requested, bool highContrast)
			=> highContrast || !BackgroundAlpha || (requested == BackdropMode.Blur && !Blur) ? BackdropMode.Solid : requested;

	}
}
