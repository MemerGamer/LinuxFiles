// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;
using System.IO;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Files.App.ViewModels.UserControls
{
	public sealed partial class NavigationToolbarViewModel
	{
		/// <summary>
		/// Enumerates subfolders using Win32 API, including hidden folders based on user settings.
		/// Returns null if the path cannot be enumerated with Win32.
		/// </summary>
		private unsafe List<(string Name, string Path, bool IsHidden)>? GetSubfolders(string parentPath)
		{
			WIN32_FIND_DATAW findData = default;
			using FindCloseSafeHandle hFile = PInvoke.FindFirstFileEx(
				$"{parentPath}{Path.DirectorySeparatorChar}*.*",
				FINDEX_INFO_LEVELS.FindExInfoBasic,
				&findData,
				FINDEX_SEARCH_OPS.FindExSearchNameMatch,
				FIND_FIRST_EX_FLAGS.FIND_FIRST_EX_LARGE_FETCH);

			if (hFile.IsInvalid)
				return null;

			var showHidden = UserSettingsService.FoldersSettingsService.ShowHiddenItems;
			var showSystem = UserSettingsService.FoldersSettingsService.ShowProtectedSystemFiles;
			var showDot = UserSettingsService.FoldersSettingsService.ShowDotFiles;
			var folders = new List<(string Name, string Path, bool IsHidden)>();

			do
			{
				if (((FileAttributes)findData.dwFileAttributes & FileAttributes.Directory) == 0)
					continue;

				string fileName = findData.cFileName.ToString();
				if (fileName is "." or "..")
					continue;

				bool isHidden = ((FileAttributes)findData.dwFileAttributes & FileAttributes.Hidden) != 0;
				bool isSystem = ((FileAttributes)findData.dwFileAttributes & FileAttributes.System) != 0;

				if (isHidden && (!showHidden || (isSystem && !showSystem)))
					continue;

				if (fileName.StartsWith('.') && !showDot)
					continue;

				folders.Add((fileName, Path.Combine(parentPath, fileName), isHidden));
			}
			while (PInvoke.FindNextFile(hFile, out findData));

			var naturalComparer = NaturalStringComparer.GetForProcessor();
			folders.Sort((a, b) => naturalComparer.Compare(a.Name, b.Name));

			return folders;
		}
	}
}
