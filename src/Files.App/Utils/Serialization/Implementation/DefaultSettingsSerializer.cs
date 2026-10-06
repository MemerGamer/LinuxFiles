// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
#if WINDOWS
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using static Files.App.Helpers.Win32Helper;
using static Files.App.Helpers.Win32PInvoke;
#else
using Files.Platform.Linux.Native;
#endif

namespace Files.App.Utils.Serialization.Implementation
{
	internal sealed class DefaultSettingsSerializer : ISettingsSerializer
	{
#if !WINDOWS
		private static readonly TimeSpan LockTimeout = TimeSpan.FromMilliseconds(250);

#endif
		private readonly Action<string>? _logWarning;
		private string? _filePath;

		public DefaultSettingsSerializer(Action<string>? logWarning = null)
		{
			_logWarning = logWarning;
		}

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

		public bool WithWriteLock(Func<bool> writeSettings)
		{
			ArgumentNullException.ThrowIfNull(_filePath);

#if !WINDOWS
			// flock on a private lock file next to the settings file serializes the read/merge/write across windows (processes).
			// The wait is bounded so a stuck holder cannot freeze the UI; the write itself stays atomic without the lock.
			using var fileLock = FileLockNative.TryAcquire(_filePath + ".lock", LockTimeout, out var error);
			if (fileLock is null)
				_logWarning?.Invoke($"Saving settings without the cross-process lock: {error}");

			return writeSettings();
#else
			var path = Path.GetFullPath(_filePath).ToUpperInvariant();

			// Serialize the entire read/merge/write across processes sharing this settings file.
			var name = "Files.Settings." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
			using var mutex = new Mutex(false, name);
			try
			{
				mutex.WaitOne();
			}
			catch (AbandonedMutexException)
			{
				// The previous writer exited; this thread now owns the mutex.
				_logWarning?.Invoke("The previous settings writer exited while holding the settings lock.");
			}

			try
			{
				return writeSettings();
			}
			finally
			{
				mutex.ReleaseMutex();
			}
#endif
		}

		public bool WriteToFile(string text)
		{
			ArgumentNullException.ThrowIfNull(_filePath);

#if !WINDOWS
			try
			{
				// Write to a temp file and rename so a crash never leaves a truncated settings file;
				// the temp name is per process because several instances (new windows) share the settings folder
				var tmp = $"{_filePath}.{Environment.ProcessId}.tmp";
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
