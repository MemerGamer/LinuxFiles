// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using System.IO;
using System.Text;

namespace Files.App.Helpers
{
	/// <summary>
	/// Private (0700) scratch space for archive members that are extracted so they can be opened.
	/// Each process uses its own <c>p&lt;pid&gt;</c> directory; leftovers of dead processes are removed at start.
	/// </summary>
	internal static class ArchiveOpenTempStore
	{
		private static readonly object gate = new();
		private static string? processDirectory;

		private static string Root
		{
			get
			{
				var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
				var baseDir = !string.IsNullOrEmpty(runtime) && Path.IsPathRooted(runtime) && Directory.Exists(runtime)
					? Path.Combine(runtime, "linuxfiles")
					: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "linuxfiles");
				return Path.Combine(baseDir, "archive-open");
			}
		}

		/// <summary>Creates a fresh private directory and returns the path for a sanitized file name inside it.</summary>
		public static string CreateFilePath(string entryName)
		{
			lock (gate)
			{
				if (processDirectory is null)
				{
					Directory.CreateDirectory(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
					processDirectory = Path.Combine(Root, "p" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
				}

				var directory = Path.Combine(processDirectory, Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(processDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
				Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
				return Path.Combine(directory, SanitizeName(entryName));
			}
		}

		/// <summary>Keeps only the last path segment, with control and separator characters replaced.</summary>
		public static string SanitizeName(string entryPath)
		{
			var name = entryPath.Replace('\\', '/');
			name = name[(name.LastIndexOf('/') + 1)..];
			var builder = new StringBuilder(name.Length);
			foreach (var c in name)
				builder.Append(char.IsControl(c) || c == '/' || c == '\0' ? '_' : c);

			var result = builder.ToString().Trim();
			if (result.Length > 120)
				result = result[..120];
			return result is "" or "." or ".." ? "file" : result;
		}

		/// <summary>Removes this process's extracted files.</summary>
		public static void CleanupCurrentProcess()
		{
			string? directory;
			lock (gate)
				directory = processDirectory;

			TryDelete(directory);
		}

		/// <summary>Removes directories left behind by processes that no longer exist.</summary>
		public static void CleanupStale()
		{
			try
			{
				var root = Root;
				if (!Directory.Exists(root))
					return;

				foreach (var directory in Directory.EnumerateDirectories(root, "p*"))
				{
					var name = Path.GetFileName(directory);
					if (int.TryParse(name.AsSpan(1), out var pid) && pid != Environment.ProcessId && Directory.Exists("/proc/" + pid.ToString(System.Globalization.CultureInfo.InvariantCulture)))
						continue;

					if (pid == Environment.ProcessId)
						continue;

					TryDelete(directory);
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}
		}

		private static void TryDelete(string? directory)
		{
			if (directory is null)
				return;

			try
			{
				if (Directory.Exists(directory))
					Directory.Delete(directory, recursive: true);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}
		}
	}
}
#endif
