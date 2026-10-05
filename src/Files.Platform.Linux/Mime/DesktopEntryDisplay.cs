// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Files.Platform.Linux.Launching;

namespace Files.Platform.Linux.Mime
{
	/// <summary>
	/// Reads the display-only fields of a .desktop file (localized name and icon) for folder listings. Nothing here executes
	/// or resolves the entry; launching stays behind the launcher confirmation.
	/// </summary>
	public static class DesktopEntryDisplay
	{
		/// <summary>
		/// Files larger than this are not parsed; real desktop files are a few kilobytes.
		/// </summary>
		public const int MaxFileSize = 64 * 1024;

		public sealed record Info(string Name, string? Icon);

		/// <summary>
		/// Returns the sanitized name and the Icon value, or <see langword="null"/> when the file is not a valid, unambiguous
		/// Application entry (the caller then shows the file name).
		/// </summary>
		public static Info? TryRead(string path, CultureInfo culture)
		{
			try
			{
				var info = new FileInfo(path);
				if (!info.Exists || info.Length > MaxFileSize)
					return null;

				var lines = File.ReadAllLines(path);
				var entry = DesktopEntryParser.ParseStrict(lines, path, Path.GetFileName(path), culture, out _);
				if (entry is null)
					return null;

				var name = DisplaySanitizer.Field(entry.Application.Name);
				if (string.IsNullOrWhiteSpace(name))
					return null;

				var icon = entry.Application.IconName;
				return new Info(name, string.IsNullOrWhiteSpace(icon) || icon.Any(char.IsControl) ? null : icon);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
			{
				return null;
			}
		}

		public static bool IsDesktopFile(string? path)
			=> path is not null && path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase);
	}
}
