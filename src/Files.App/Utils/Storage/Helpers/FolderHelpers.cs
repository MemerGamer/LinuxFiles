// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App.Utils.Storage
{
	public readonly record struct SubfolderEntry(string Path, string Name, bool HasSubfolders, bool IsHidden);

	public static class FolderHelpers
	{
		public static unsafe bool CheckFolderAccessWithWin32(string path)
		{
			return SafeEnumerates(path);
		}

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
			try
			{
				using var e = Directory.EnumerateFileSystemEntries(targetPath).GetEnumerator();
				return e.MoveNext();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return false;
			}
		}

		public static unsafe List<SubfolderEntry> EnumerateSubfolders(string path, bool showHidden, bool showProtected, bool showDot, int limit = 1000)
		{
			var results = new List<SubfolderEntry>();
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
		}

		public static unsafe bool HasSubfolders(string path)
		{
			try
			{
				using var e = Directory.EnumerateDirectories(path).GetEnumerator();
				return e.MoveNext();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return false;
			}
		}
	}
}
