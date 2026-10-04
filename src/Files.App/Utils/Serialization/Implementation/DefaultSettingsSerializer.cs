// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using static Files.App.Helpers.Win32Helper;
using static Files.App.Helpers.Win32PInvoke;

namespace Files.App.Utils.Serialization.Implementation
{
	internal sealed class DefaultSettingsSerializer : ISettingsSerializer
	{
		private string? _filePath;

		public bool CreateFile(string path)
		{
#if !WINDOWS
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				using (new FileStream(path, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite))
				{
				}

				_filePath = path;
				return true;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return false;
			}
#else
			PInvoke.CreateDirectoryFromApp(Path.GetDirectoryName(path), null);

			var hFile = CreateFileFromApp(path, (uint)FILE_ACCESS_RIGHTS.FILE_GENERIC_READ, FILE_SHARE_READ, IntPtr.Zero, OPEN_ALWAYS, (uint)File_Attributes.BackupSemantics, IntPtr.Zero);
			if (hFile.IsHandleInvalid())
			{
				return false;
			}

			Win32PInvoke.CloseHandle(hFile);

			_filePath = path;
			return true;
#endif
		}

		/// <summary>
		/// Reads a file to a string
		/// </summary>
		/// <returns>A string value or string.Empty if nothing is present in the file</returns>
		/// <exception cref="ArgumentNullException"></exception>
		public string ReadFromFile()
		{
			ArgumentNullException.ThrowIfNull(_filePath);

#if !WINDOWS
			try
			{
				return File.ReadAllText(_filePath);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return string.Empty;
			}
#else
			return ReadStringFromFile(_filePath) ?? string.Empty;
#endif
		}

		public bool WriteToFile(string text)
		{
			ArgumentNullException.ThrowIfNull(_filePath);

#if !WINDOWS
			try
			{
				// Write to a temp file and rename so a crash never leaves a truncated settings file
				var tmp = _filePath + ".tmp";
				File.WriteAllText(tmp, text);
				File.Move(tmp, _filePath, true);
				return true;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return false;
			}
#else
			return WriteStringToFile(_filePath, text);
#endif
		}
	}
}
