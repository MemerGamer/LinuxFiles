// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App.Utils.Shell
{
	public static class ShellHelpers
	{
		public static string ResolveShellPath(string shPath)
		{
			return shPath;
		}

		public static bool IsSupportedShellPath(string shPath)
		{
			return false;
		}

		private static string GetShellPathLookupKey(string shPath)
		{
			var lookupPath = shPath.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase)
				? shPath.Substring("shell:".Length)
				: shPath;

			return lookupPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
		}

		public static string GetShellNameFromPath(string shPath)
		{
			return shPath switch
			{
				"Home" => Strings.Home.GetLocalizedResource(),
				"ReleaseNotes" => Strings.ReleaseNotes.GetLocalizedResource(),
				"Settings" => Strings.Settings.GetLocalizedResource(),
				Constants.UserEnvironmentPaths.RecycleBinPath => Strings.RecycleBin.GetLocalizedResource(),
				Constants.UserEnvironmentPaths.NetworkFolderPath => Strings.Network.GetLocalizedResource(),
				Constants.UserEnvironmentPaths.MyComputerPath => Strings.ThisPC.GetLocalizedResource(),
				_ => shPath
			};
		}

		public static string GetLibraryFullPathFromShell(string shPath)
		{
			var partialPath = shPath.Substring(shPath.IndexOf('\\') + 1);
			return Path.Combine(ShellLibraryItem.LibrariesPath, partialPath);
		}
	}
}
