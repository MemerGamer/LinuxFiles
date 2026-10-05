// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Fonts;
using Files.Platform.Linux.Elevation;
using Files.Platform.Linux.Launching;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Fonts
{
	/// <summary>
	/// Installs fonts into the user's font folder (<c>$XDG_DATA_HOME/fonts</c>) and refreshes the cache with <c>fc-cache</c> (argument vector, no shell).
	/// Only files whose first bytes identify a font are copied, and an existing font is never replaced without explicit permission.
	/// </summary>
	public sealed class UserFontInstallService : IFontInstallService
	{
		private readonly string fontsDirectory;
		private readonly IExecutableLocator locator;
		private readonly IElevatedProcessRunner runner;

		/// <summary>
		/// Creates the service. <paramref name="fontsDirectory"/> is the user's font folder.
		/// </summary>
		public UserFontInstallService(string fontsDirectory, IExecutableLocator locator, IElevatedProcessRunner runner)
		{
			this.fontsDirectory = fontsDirectory;
			this.locator = locator;
			this.runner = runner;
		}

		/// <summary>Gets the default user font folder.</summary>
		public static string DefaultFontsDirectory(Func<string, string?> getEnvironmentVariable)
		{
			var dataHome = getEnvironmentVariable("XDG_DATA_HOME");
			if (string.IsNullOrEmpty(dataHome) || !dataHome.StartsWith('/'))
				dataHome = Path.Combine(getEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

			return Path.Combine(dataHome, "fonts");
		}

		/// <inheritdoc/>
		public bool IsInstalled(string sourcePath)
			=> GetSafeFileName(sourcePath) is { } name && File.Exists(Path.Combine(fontsDirectory, name));

		/// <inheritdoc/>
		public async Task<FontInstallResult> InstallAsync(string sourcePath, bool overwrite, CancellationToken cancellationToken = default)
		{
			if (GetSafeFileName(sourcePath) is not { } name)
				return FontInstallResult.Failed;

			try
			{
				if (!await HasFontSignatureAsync(sourcePath, cancellationToken).ConfigureAwait(false))
					return FontInstallResult.NotAFont;

				Directory.CreateDirectory(fontsDirectory);
				var destination = Path.Combine(fontsDirectory, name);

				// A symlink at the destination must never redirect the write elsewhere
				if (File.Exists(destination) || Directory.Exists(destination))
				{
					if (new FileInfo(destination).LinkTarget is not null || !overwrite)
						return overwrite ? FontInstallResult.Failed : FontInstallResult.AlreadyExists;
				}

				var temporary = destination + ".part-" + Guid.NewGuid().ToString("N")[..8];
				try
				{
					File.Copy(sourcePath, temporary, overwrite: false);
					File.Move(temporary, destination, overwrite);
				}
				finally
				{
					if (File.Exists(temporary))
						File.Delete(temporary);
				}

				await RefreshCacheAsync(cancellationToken).ConfigureAwait(false);
				return FontInstallResult.Installed;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return FontInstallResult.Failed;
			}
		}

		private async Task RefreshCacheAsync(CancellationToken cancellationToken)
		{
			if (locator.Locate("fc-cache") is not { } fcCache)
				return;

			try
			{
				await runner.RunAsync(fcCache, ["-f", fontsDirectory], cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
			{
				// The font is installed; applications pick it up after the next cache refresh
			}
		}

		private static string? GetSafeFileName(string sourcePath)
		{
			if (string.IsNullOrEmpty(sourcePath) || sourcePath.Contains('\0'))
				return null;

			var name = Path.GetFileName(sourcePath);
			return string.IsNullOrEmpty(name) || name is "." or ".." || name.StartsWith('.') ? null : name;
		}

		private static async Task<bool> HasFontSignatureAsync(string path, CancellationToken cancellationToken)
		{
			var header = new byte[4];
			await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous);
			var read = 0;
			while (read < header.Length)
			{
				var count = await stream.ReadAsync(header.AsMemory(read), cancellationToken).ConfigureAwait(false);
				if (count == 0)
					return false;
				read += count;
			}

			return IsFontSignature(header);
		}

		/// <summary>Whether the first four bytes identify TrueType, OpenType, TrueType Collection, WOFF or WOFF2.</summary>
		public static bool IsFontSignature(ReadOnlySpan<byte> header)
			=> header.Length >= 4 && (
				header.SequenceEqual<byte>([0x00, 0x01, 0x00, 0x00]) ||
				header.SequenceEqual<byte>("true"u8) ||
				header.SequenceEqual<byte>("OTTO"u8) ||
				header.SequenceEqual<byte>("ttcf"u8) ||
				header.SequenceEqual<byte>("wOFF"u8) ||
				header.SequenceEqual<byte>("wOF2"u8));
	}
}
