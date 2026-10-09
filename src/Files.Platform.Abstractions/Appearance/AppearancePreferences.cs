// Copyright (c) Files Community
// Licensed under the MIT License.

using System;

namespace Files.Platform.Abstractions.Appearance
{
	public static class AppearancePreferences
	{
		public static float ClampOpacity(float value, float minimum = 0)
			=> float.IsFinite(value) ? Math.Clamp(value, minimum, 1) : 1;

		public static ColourSource MigrateColourSource(string? stored, bool adwaita)
			=> Enum.TryParse<ColourSource>(stored, out var source) && Enum.IsDefined(source)
				? source : adwaita ? ColourSource.Adwaita : ColourSource.Files;
	}
}
