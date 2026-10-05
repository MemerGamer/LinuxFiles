// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
#if WINDOWS
using Windows.Win32;
#endif
#if WINDOWS
using Windows.Win32.Storage.FileSystem;
#endif

namespace Files.App.Utils.Storage
{
	public readonly record struct SubfolderEntry(string Path, string Name, bool HasSubfolders, bool IsHidden);

	public static class FolderHelpers
	{
		public static unsafe bool CheckFolderAccessWithWin32(string path)
		{
#if !WINDOWS
			return SafeEnumerates(path);
#else
			WIN32_FIND_DATAW findData = default;
			using FindCloseSafeHandle hFile = PInvoke.FindFirstFileEx($"{path}{Path.DirectorySeparatorChar}*.*", FINDEX_INFO_LEVELS.FindExInfoBasic,
				&findData, FINDEX_SEARCH_OPS.FindExSearchNameMatch, FIND_FIRST_EX_FLAGS.FIND_FIRST_EX_LARGE_FETCH);
			return !hFile.IsInvalid;
#endif
		}

#if !WINDOWS
		// Opening the directory for listing is the access test (an empty readable folder is accessible)
		private static bool SafeEnumerates(string path)
		{
			try
			{
				using var e = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
				e.MoveNext();
				return true;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return false;
			}
		}
#endif

		public static async Task<bool> CheckBitlockerStatusAsync(BaseStorageFolder? rootFolder, string path)
		{
			if (rootFolder?.Properties is null)
			{
				return false;
			}
			if (Path.IsPathRooted(path) && Path.GetPathRoot(path) == path)
			{
				IDictionary<string, object> extraProperties =
					await rootFolder.Properties.RetrievePropertiesAsync((string[])["System.Volume.BitLockerProtection"]);
				return (int?)extraProperties["System.Volume.BitLockerProtection"] == 6; // Drive is bitlocker protected and locked
			}
			return false;
		}

		/// <summary>
		/// This function is used to determine whether or not a folder has any contents.
		/// </summary>
		/// <param name="targetPath">The path to the target folder</param>
		///
		public static unsafe bool CheckForFilesFolders(string targetPath)
		{
#if !WINDOWS
			try
			{
				using var e = Directory.EnumerateFileSystemEntries(targetPath).GetEnumerator();
				return e.MoveNext();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return false;
			}
#else
			WIN32_FIND_DATAW findData = default;
			using FindCloseSafeHandle hFile = PInvoke.FindFirstFileEx($"{targetPath}{Path.DirectorySeparatorChar}*.*", FINDEX_INFO_LEVELS.FindExInfoBasic,
				&findData, FINDEX_SEARCH_OPS.FindExSearchNameMatch, FIND_FIRST_EX_FLAGS.FIND_FIRST_EX_LARGE_FETCH);
			if (hFile.IsInvalid)
				return false;

			do
			{
				string fileName = findData.cFileName.ToString();
				if (fileName is not "." and not "..")
					return true;
			}
			while (PInvoke.FindNextFile(hFile, out findData));

			return false;
#endif
		}

		public static unsafe List<SubfolderEntry> EnumerateSubfolders(string path, bool showHidden, bool showProtected, bool showDot, int limit = 1000)
		{
			var results = new List<SubfolderEntry>();
#if !WINDOWS
			try
			{
				foreach (var dir in new DirectoryInfo(path).EnumerateDirectories())
				{
					var isHidden = dir.Name.StartsWith('.');
					if (isHidden && !showDot)
						continue;
					if (isHidden && !showHidden)
						continue;

					results.Add(new SubfolderEntry(dir.FullName, dir.Name, HasSubfolders(dir.FullName), isHidden));

					if (results.Count == limit)
						break;
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}

			var comparer = NaturalStringComparer.GetForProcessor();
			results.Sort((a, b) => comparer.Compare(a.Name, b.Name));
			return results;
#else
			WIN32_FIND_DATAW findData = default;
			using FindCloseSafeHandle hFind = PInvoke.FindFirstFileEx(
				path + "\\*.*",
				FINDEX_INFO_LEVELS.FindExInfoBasic,
				&findData,
				FINDEX_SEARCH_OPS.FindExSearchNameMatch,
				FIND_FIRST_EX_FLAGS.FIND_FIRST_EX_LARGE_FETCH);
			if (hFind.IsInvalid)
				return results;

			do
			{
				var attrs = (FileAttributes)findData.dwFileAttributes;
				if ((attrs & FileAttributes.Directory) != FileAttributes.Directory)
					continue;

				string fileName = findData.cFileName.ToString();
				if (fileName is "." or "..")
					continue;

				var isHidden = (attrs & FileAttributes.Hidden) == FileAttributes.Hidden;
				var isSystem = (attrs & FileAttributes.System) == FileAttributes.System;

				if (!showDot && fileName.StartsWith('.'))
					continue;
				if (isHidden && !showHidden)
					continue;
				if (isHidden && isSystem && !showProtected)
					continue;

				var subPath = Path.Combine(path, fileName);
				results.Add(new SubfolderEntry(subPath, fileName, HasSubfolders(subPath), isHidden));

				if (results.Count == limit)
					break;
			}
			while (PInvoke.FindNextFile(hFind, out findData));

			var naturalComparer = NaturalStringComparer.GetForProcessor();
			results.Sort((a, b) => naturalComparer.Compare(a.Name, b.Name));
			return results;
#endif
		}

		public static unsafe bool HasSubfolders(string path)
		{
#if !WINDOWS
			try
			{
				using var e = Directory.EnumerateDirectories(path).GetEnumerator();
				return e.MoveNext();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return false;
			}
#else
			WIN32_FIND_DATAW findData = default;
			using FindCloseSafeHandle hFind = PInvoke.FindFirstFileEx(
				path + "\\*.*",
				FINDEX_INFO_LEVELS.FindExInfoBasic,
				&findData,
				FINDEX_SEARCH_OPS.FindExSearchNameMatch,
				FIND_FIRST_EX_FLAGS.FIND_FIRST_EX_LARGE_FETCH);
			if (hFind.IsInvalid)
				return false;

			do
			{
				string fileName = findData.cFileName.ToString();
				if (fileName is "." or "..")
					continue;
				if (((FileAttributes)findData.dwFileAttributes & FileAttributes.Directory) == FileAttributes.Directory)
					return true;
			}
			while (PInvoke.FindNextFile(hFind, out findData));
			return false;
#endif
		}
	}
}
