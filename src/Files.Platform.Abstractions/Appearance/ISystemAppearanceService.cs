// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Appearance
{
	public enum BackdropMode { Solid, Transparent, Blur }
	public enum ColourSource { Files, Adwaita, System }

	/// <summary>An sRGB colour independent of the UI framework.</summary>
	public readonly record struct AppearanceColor(byte R, byte G, byte B, byte A = 255);

	/// <summary>Unset portal preferences and missing GTK roles retain the application's defaults.</summary>
	public sealed record SystemAppearance(bool? IsDark, bool? HighContrast, AppearanceColor? Accent,
		IReadOnlyDictionary<string, AppearanceColor> NamedColors, bool PaletteIsDark = false);

	public interface ISystemAppearanceService
	{
		SystemAppearance Current { get; }
		event EventHandler? Changed;
		Task RefreshAsync(CancellationToken cancellationToken = default);
	}
}
