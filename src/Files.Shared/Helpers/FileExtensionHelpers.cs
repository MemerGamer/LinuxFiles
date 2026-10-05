// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;

namespace Files.Shared.Helpers
{
	/// <summary>
	/// Provides static extension for path extension.
	/// </summary>
	public static class FileExtensionHelpers
	{
		private static readonly FrozenSet<string> _signableTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".aab", ".apk", ".application", ".appx", ".appxbundle", ".arx", ".cab", ".cat", ".cbx",
			".cpl", ".crx", ".dbx", ".deploy", ".dll", ".doc", ".docm", ".dot", ".dotm", ".drx",
			".ear", ".efi", ".exe", ".jar", ".js", ".manifest", ".mpp", ".mpt", ".msi", ".msix",
			".msixbundle", ".msm", ".msp", ".nupkg", ".ocx", ".pot", ".potm", ".ppa", ".ppam", ".pps",
			".ppsm", ".ppt", ".pptm", ".ps1", ".psm1", ".psi", ".pub", ".sar", ".stl", ".sys", ".vbs",
			".vdw", ".vdx", ".vsd", ".vsdm", ".vss", ".vssm", ".vst", ".vstm", ".vsto", ".vsix", ".vsx", ".vtx",
			".vxd", ".war", ".wiz", ".wsf", ".xap", ".xla", ".xlam", ".xls", ".xlsb", ".xlsm", ".xlt",
			".xltm", ".xlsm", ".xsn"
		}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Check if the file extension matches one of the specified extensions.
		/// </summary>
		/// <param name="filePathToCheck">Path or name or extension of the file to check.</param>
		/// <param name="extensions">List of the extensions to check.</param>
		/// <returns><c>true</c> if the filePathToCheck has one of the specified extensions; otherwise, <c>false</c>.</returns>
		public static bool HasExtension(string? filePathToCheck, params ReadOnlySpan<string> extensions)
		{
			if (string.IsNullOrWhiteSpace(filePathToCheck))
				return false;

			// Don't check folder paths to avoid issues
			// https://github.com/files-community/Files/issues/17094
			if (Directory.Exists(filePathToCheck))
				return false;

			string pathExtension = Path.GetExtension(filePathToCheck);
			foreach (string ext in extensions)
				if (pathExtension.Equals(ext, StringComparison.OrdinalIgnoreCase))
					return true;

			return false;
		}

		/// <summary>
		/// Check if the file extension is an image file.
		/// </summary>
		/// <param name="fileExtensionToCheck">The file extension to check.</param>
		/// <returns><c>true</c> if the fileExtensionToCheck is an image; otherwise, <c>false</c>.</returns>
		public static bool IsImageFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".png", ".bmp", ".jpg", ".jpeg", ".jfif", ".gif", ".tiff", ".tif", ".webp", ".jxr");
		}

		/// <summary>
		/// Check if the file extension is an image that can be converted to an ICO file.
		/// </summary>
		/// <param name="fileExtensionToCheck">The file extension to check.</param>
		/// <returns><c>true</c> if the fileExtensionToCheck can be converted to an ICO file; otherwise, <c>false</c>.</returns>
		public static bool IsConvertibleToIcoFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".png", ".bmp", ".jpg", ".jpeg", ".jfif");
		}

		/// <summary>
		/// Checks if the file can be set as wallpaper.
		/// </summary>
		/// <param name="fileExtensionToCheck">The file extension to check.</param>
		/// <returns><c>true</c> if the fileExtensionToCheck is an image; otherwise, <c>false</c>.</returns>
		public static bool IsCompatibleToSetAsWindowsWallpaper(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".png", ".bmp", ".jpg", ".jpeg", ".jfif", ".gif", ".tiff", ".tif", ".jxr");
		}

		/// <summary>
		/// Check if the file extension is an audio file.
		/// </summary>
		/// <param name="fileExtensionToCheck">The file extension to check.</param>
		/// <returns><c>true</c> if the fileExtensionToCheck is an audio file; otherwise, <c>false</c>.</returns>
		public static bool IsAudioFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".mp3", ".m4a", ".ogg", ".oga", ".wav", ".wma", ".aac", ".adt", ".adts", ".cda", ".flac");
		}

		/// <summary>
		/// Check if the file extension is a video file.
		/// </summary>
		/// <param name="fileExtensionToCheck">The file extension to check.</param>
		/// <returns><c>true</c> if the fileExtensionToCheck is a video file; otherwise, <c>false</c>.</returns>
		public static bool IsVideoFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".avi", ".mp4", ".webm", ".ogg", ".mov", ".qt", ".m4v", ".mp4v", ".3g2", ".3gp2", ".3gp", ".3gpp", ".mkv");
		}

		/// <summary>
		/// Check if the file extension is a PowerShell script.
		/// </summary>
		/// <param name="fileExtensionToCheck">The file extension to check.</param>
		/// <returns><c>true</c> if the fileExtensionToCheck is a PowerShell script; otherwise, <c>false</c>.</returns>
		public static bool IsPowerShellFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".ps1");
		}

		/// <summary>
		/// Check if the file extension is a Batch file.
		/// </summary>
		/// <param name="fileExtensionToCheck">The file extension to check.</param>
		/// <returns><c>true</c> if the fileExtensionToCheck is a Batch file; otherwise, <c>false</c>.</returns>
		public static bool IsBatchFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".bat");
		}

		/// <summary>
		/// Check if the file extension is a zip file.
		/// </summary>
		/// <param name="fileExtensionToCheck">The file extension to check.</param>
		/// <returns><c>true</c> if the fileExtensionToCheck is a zip bundle file; otherwise, <c>false</c>.</returns>
		public static bool IsZipFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".zip", ".msix", ".appx", ".msixbundle", ".appxbundle", ".7z", ".rar", ".tar", ".mcpack", ".mcworld", ".mrpack", ".jar", ".gz", ".lzh", ".tgz", ".bz2", ".xz", ".zst", ".tbz2", ".txz", ".tzst");
		}

		public static bool IsBrowsableZipFile(string? filePath, [NotNullWhen(true)] out string? ext)
		{
			if (string.IsNullOrWhiteSpace(filePath))
			{
				ext = null;

				return false;
			}

			if (OperatingSystem.IsWindows())
			{
				// Windows keeps the original substring matching
				ext = new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".lzh", ".mrpack", ".jar" }
					.FirstOrDefault(x => filePath.Contains(x, StringComparison.OrdinalIgnoreCase));

				return ext is not null;
			}

			ext = null;
			foreach (var component in filePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
			{
				var candidate = Path.GetExtension(component);
				if (new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".zst", ".tgz", ".tbz2", ".txz", ".tzst", ".mrpack", ".jar" }.Contains(candidate, StringComparer.OrdinalIgnoreCase))
				{
					ext = candidate;
					break;
				}
			}
			return ext is not null;
		}

		/// <summary>Finds a browsable archive component, skipping real directories with archive extensions.</summary>
		public static string? GetArchiveContainerPath(string path)
		{
			for (var end = 1; end <= path.Length; end++)
			{
				if (end < path.Length && path[end] is not ('/' or '\\'))
					continue;
				var candidate = path[..end];
				var name = candidate[(Math.Max(candidate.LastIndexOf('/'), candidate.LastIndexOf('\\')) + 1)..];
				if (IsBrowsableZipFile(name, out _) && !Directory.Exists(candidate))
					return candidate;
			}
			return null;
		}

		/// <summary>Checks for an archive root or a member path using either separator.</summary>
		public static bool IsZipPath([NotNullWhen(true)] string? path, bool includeRoot = true)
		{
			if (string.IsNullOrEmpty(path) || GetArchiveContainerPath(path) is not { } container)
				return false;
			return container.Length == path.TrimEnd('/', '\\').Length ? includeRoot : !Path.Exists(path);
		}

		/// <summary>
		/// Check if the file extension is a driver inf file.
		/// </summary>
		/// <param name="fileExtensionToCheck">The file extension to check.</param>
		/// <returns><c>true</c> if the fileExtensionToCheck is an inf file; otherwise <c>false</c>.</returns>
		public static bool IsInfFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".inf");
		}

		/// <summary>
		/// Check if the file extension is a font file.
		/// </summary>
		/// <param name="fileExtensionToCheck">The file extension to check.</param>
		/// <returns><c>true</c> if the fileExtensionToCheck is a font file; otherwise <c>false</c>.</returns>
		/// <remarks>Font file types are; fon, otf, ttc, ttf</remarks>
		public static bool IsFontFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".fon", ".otf", ".ttc", ".ttf");
		}

		/// <summary>
		/// Check if the file path is a shortcut file.
		/// </summary>
		/// <param name="filePathToCheck">The file path to check.</param>
		/// <returns><c>true</c> if the filePathToCheck is a shortcut file; otherwise, <c>false</c>.</returns>
		/// <remarks>Shortcut file type is .lnk</remarks>
		public static bool IsShortcutFile(string? filePathToCheck)
		{
			return HasExtension(filePathToCheck, ".lnk");
		}

		/// <summary>
		/// Check if the file path is a web link file.
		/// </summary>
		/// <param name="filePathToCheck">The file path to check.</param>
		/// <returns><c>true</c> if the filePathToCheck is a web link file; otherwise, <c>false</c>.</returns>
		/// <remarks>Web link file type is .url</remarks>
		public static bool IsWebLinkFile(string? filePathToCheck)
		{
			return HasExtension(filePathToCheck, ".url");
		}

		public static bool IsShortcutOrUrlFile(string? filePathToCheck)
		{
			return HasExtension(filePathToCheck, ".lnk", ".url");
		}

		/// <summary>
		/// Check if the file path is an executable file.
		/// </summary>
		/// <param name="filePathToCheck">The file path to check.</param>
		/// <returns><c>true</c> if the filePathToCheck is an executable file; otherwise, <c>false</c>.</returns>
		/// /// <remarks>Executable file types are; exe, bat, cmd</remarks>
		public static bool IsExecutableFile(string? filePathToCheck, bool exeOnly = false)
		{
			return
				exeOnly
					? HasExtension(filePathToCheck, ".exe")
					: HasExtension(filePathToCheck, ".exe", ".bat", ".cmd", ".ahk");
		}

		/// <summary>
		/// Check if the file path is an Auto Hot Key file.
		/// </summary>
		/// <param name="filePathToCheck">The file path to check.</param>
		/// <returns><c>true</c> if the filePathToCheck is an Auto Hot Key file; otherwise, <c>false</c>.</returns>
		public static bool IsAhkFile(string? filePathToCheck)
		{
			return HasExtension(filePathToCheck, ".ahk");
		}

		/// <summary>
		/// Check if the file path is a cmd file.
		/// </summary>
		/// <param name="filePathToCheck">The file path to check.</param>
		/// <returns><c>true</c> if the filePathToCheck is a cmd file; otherwise, <c>false</c>.</returns>
		public static bool IsCmdFile(string? filePathToCheck)
		{
			return HasExtension(filePathToCheck, ".cmd");
		}

		/// <summary>
		/// Check if the file path is an msi installer file.
		/// </summary>
		/// <param name="filePathToCheck">The file path to check.</param>
		/// <returns><c>true</c> if the filePathToCheck is an msi installer file; otherwise, <c>false</c>.</returns>
		public static bool IsMsiFile(string? filePathToCheck)
		{
			return HasExtension(filePathToCheck, ".msi");
		}

		/// <summary>
		/// Check if the file extension is a vhd disk file.
		/// </summary>
		/// <param name="fileExtensionToCheck">The file extension to check.</param>
		/// <returns><c>true</c> if the fileExtensionToCheck is a vhd disk file; otherwise, <c>false</c>.</returns>
		/// <remarks>Vhd disk file types are; vhd, vhdx</remarks>
		public static bool IsVhdFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".vhd", ".vhdx");
		}

		/// <summary>
		/// Check if the file extension is a screen saver file.
		/// </summary>
		/// <param name="fileExtensionToCheck">The file extension to check.</param>
		/// <returns><c>true</c> if the fileExtensionToCheck is a screen saver file; otherwise, <c>false</c>.</returns>
		/// <remarks>Screen saver file types are; scr</remarks>
		public static bool IsScreenSaverFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".scr");
		}

		/// <summary>
		/// Check if the file extension is a media (audio/video) file.
		/// </summary>
		/// <param name="filePathToCheck">The file extension to check.</param>
		/// <returns><c>true</c> if the filePathToCheck is a media file; otherwise, <c>false</c>.</returns>
		public static bool IsMediaFile(string? filePathToCheck)
		{
			return HasExtension(
				filePathToCheck, ".mp4", ".m4v", ".mp4v", ".3g2", ".3gp2", ".3gp", ".3gpp",
				".mpg", ".mp2", ".mpeg", ".mpe", ".mpv", ".mkv", ".ogg", ".avi", ".wmv", ".mov", ".qt",
				".mp3", ".m4a", ".oga", ".wav", ".wma", ".aac", ".flac");
		}

		/// <summary>
		/// Check if the file extension is a certificate file.
		/// </summary>
		/// <param name="filePathToCheck"></param>
		/// <returns><c>true</c> if the filePathToCheck is a certificate file; otherwise, <c>false</c>.</returns>
		public static bool IsCertificateFile(string? filePathToCheck)
		{
			return HasExtension(filePathToCheck, ".cer", ".crt", ".der", ".pfx");
		}

		/// <summary>
		/// Check if the file extension is a Script file.
		/// </summary>
		/// <param name="filePathToCheck"></param>
		/// <returns><c>true</c> if the filePathToCheck is a script file; otherwise, <c>false</c>.</returns>
		public static bool IsScriptFile(string? filePathToCheck)
		{
			return HasExtension(filePathToCheck, ".py", ".ahk", ".bat", ".cmd", ".ps1");
		}

		/// <summary>
		/// Check if the file extension is a system file.
		/// </summary>
		/// <param name="filePathToCheck"></param>
		/// <returns><c>true</c> if the filePathToCheck is a system file; otherwise, <c>false</c>.</returns>
		public static bool IsSystemFile(string? filePathToCheck)
		{
			return HasExtension(filePathToCheck, ".dll", ".exe", ".sys", ".inf");
		}

		/// <summary>
		/// Check if the file is signable.
		/// </summary>
		/// <param name="filePathToCheck"></param>
		/// <returns><c>true</c> if the filePathToCheck is a signable file; otherwise, <c>false</c>.</returns>
		public static bool IsSignableFile(string? filePathToCheck, bool isExtension = false)
		{
			if (string.IsNullOrWhiteSpace(filePathToCheck))
				return false;

			if (!isExtension)
				filePathToCheck = Path.GetExtension(filePathToCheck);

			return _signableTypes.Contains(filePathToCheck);
		}

		/// <summary>
		/// Check if the file extension is a markdown file.
		/// </summary>
		/// <param name="fileExtensionToCheck"></param>
		/// <returns><c>true</c> if the fileExtensionToCheck is a markdown file; otherwise, <c>false</c>.</returns>
		public static bool IsMarkdownFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".md", ".markdown");
		}

		/// <summary>
		/// Check if the file extension is a text file.
		/// </summary>
		/// <param name="fileExtensionToCheck"></param>
		/// <returns><c>true</c> if the fileExtensionToCheck is a text file; otherwise, <c>false</c>.</returns>
		public static bool IsTextFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".txt");
		}

		/// <summary>
		/// Check if the file extension is a rich text file.
		/// </summary>
		/// <param name="fileExtensionToCheck"></param>
		/// <returns><c>true</c> if the fileExtensionToCheck is a rich text file; otherwise, <c>false</c>.</returns>
		public static bool IsRichTextFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".rtf");
		}

		/// <summary>
		/// Check if the file extension is a PDF file.
		/// </summary>
		/// <param name="fileExtensionToCheck"></param>
		/// <returns><c>true</c> if the fileExtensionToCheck is a PDF file; otherwise, <c>false</c>.</returns>
		public static bool IsPdfFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".pdf");
		}

		/// <summary>
		/// Check if the file extension is an HTML file.
		/// </summary>
		/// <param name="fileExtensionToCheck"></param>
		/// <returns><c>true</c> if the fileExtensionToCheck is an HTML file; otherwise, <c>false</c>.</returns>
		public static bool IsHtmlFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".htm", ".html", ".svg");
		}

		/// <summary>
		/// Check if the file extension is supported by the image preview pane.
		/// </summary>
		/// <param name="fileExtensionToCheck"></param>
		/// <returns><c>true</c> if the fileExtensionToCheck can be image-previewed; otherwise, <c>false</c>.</returns>
		public static bool IsImagePreviewFile(string? fileExtensionToCheck)
		{
			return HasExtension(fileExtensionToCheck, ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tiff", ".ico", ".webp", ".jxr");
		}
	}

	/// <summary>Escapes untrusted archive names without changing the identifier used to open an entry.</summary>
	public static class ArchiveDisplayName
	{
		public static string Escape(string value)
		{
			var result = new StringBuilder(value.Length);
			foreach (var character in value)
			{
				if (char.IsControl(character) || char.GetUnicodeCategory(character) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator || character is '\\' or '\u034F' or '\u180B' or '\u180C' or '\u180D')
					result.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
				else
					result.Append(character);
			}
			return result.ToString();
		}
	}
}
