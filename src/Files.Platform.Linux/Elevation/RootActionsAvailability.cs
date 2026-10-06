// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;

namespace Files.Platform.Linux.Elevation
{
	public static class RootActionsAvailability
	{
		public static bool IsDisabled => File.Exists("/.flatpak-info") ||
			File.Exists(Path.Combine(AppContext.BaseDirectory, ".root-actions-disabled")) ||
			Environment.GetEnvironmentVariable("APPIMAGE") is not null || Environment.GetEnvironmentVariable("APPDIR") is not null ||
			Environment.GetEnvironmentVariable("FILES_DISABLE_ROOT_ACTIONS") == "1";
	}
}
