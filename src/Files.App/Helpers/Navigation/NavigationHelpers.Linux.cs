// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Files.App.Dialogs;
using Files.Platform.Abstractions.Launching;
using Files.Platform.Abstractions.Mime;
using Files.Platform.Linux.Mime;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.IO;

namespace Files.App.Helpers
{
	/// <summary>
	/// Linux implementation of opening files: replaces the Win32 shell launch path of <c>OpenPath</c>.
	/// </summary>
	public static partial class NavigationHelpers
	{
		private static ILauncherService LinuxLauncher => Ioc.Default.GetRequiredService<ILauncherService>();

		private static IMimeTypeService LinuxMimeTypes => Ioc.Default.GetRequiredService<IMimeTypeService>();

		private static async Task<bool> OpenPathLinuxAsync(string path, IShellPage associatedInstance, bool openViaApplicationPicker, IEnumerable<string>? selectItems, bool forceOpenInNewTab)
		{
			try
			{
				if (Directory.Exists(path))
				{
					var foldersSettings = Ioc.Default.GetRequiredService<IUserSettingsService>().FoldersSettingsService;
					await OpenPath(forceOpenInNewTab, foldersSettings.OpenFoldersInNewTab, path, associatedInstance, selectItems);
					return true;
				}

				if (!File.Exists(path))
				{
					await DialogDisplayHelper.ShowDialogAsync(Strings.FileNotFoundDialogTitle.GetLocalizedResource(), Strings.FileNotFoundDialogText.GetLocalizedResource());
					associatedInstance.ToolbarViewModel.CanRefresh = false;
					associatedInstance.ShellViewModel?.RefreshItems(associatedInstance.ShellViewModel.WorkingDirectory);
					return false;
				}

				return await OpenFileLinuxAsync(path, openViaApplicationPicker);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				App.Logger.LogWarning(ex, "Failed to open {Path}", path);
				return false;
			}
		}

		/// <summary>
		/// Opens several files together with their default applications.
		/// </summary>
		private static Task<bool> OpenFilesLinuxAsync(IEnumerable<string> paths)
			=> LinuxLauncher.OpenAsync(paths);

		internal static async Task<bool> OpenFileLinuxAsync(string path, bool openViaApplicationPicker = false)
		{
			if (openViaApplicationPicker)
				return await LinuxOpenWithDialog.ShowAsync(path);

			var mime = await LinuxMimeTypes.GetMimeTypeAsync(path);

			if (mime == "application/x-desktop" || path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
			{
				var handled = await TryLaunchDesktopFileAsync(path);
				if (handled is not null)
					return handled.Value;
			}
			else if (HasExecuteBit(path))
			{
				var kind = DetectExecutableKind(path, mime);
				if (kind == LinuxExecutableKind.Binary)
					return await LinuxLauncher.RunExecutableAsync(path, null, Path.GetDirectoryName(path));

				if (kind == LinuxExecutableKind.Script)
				{
					var dialog = new DynamicDialog(new DynamicDialogViewModel()
					{
						TitleText = Strings.LinuxRunExecutableTitle.GetLocalizedFormatResource(Path.GetFileName(path)),
						SubtitleText = Strings.LinuxRunExecutableText.GetLocalizedResource(),
						PrimaryButtonText = Strings.Run.GetLocalizedResource(),
						SecondaryButtonText = Strings.LinuxDisplayFile.GetLocalizedResource(),
						CloseButtonText = Strings.Cancel.GetLocalizedResource(),
						DynamicButtons = DynamicDialogButtons.Primary | DynamicDialogButtons.Secondary | DynamicDialogButtons.Cancel
					});

					switch (await DialogDisplayHelper.ShowDialogAsync(dialog))
					{
						case DynamicDialogResult.Primary:
							return await LinuxLauncher.RunExecutableAsync(path, null, Path.GetDirectoryName(path));
						case DynamicDialogResult.Secondary:
							break;
						default:
							return true;
					}
				}
			}

			return await LinuxLauncher.OpenAsync([path]);
		}

		/// <returns>Null when the file is not a launchable application entry and should be opened as a regular file.</returns>
		private static async Task<bool?> TryLaunchDesktopFileAsync(string path)
		{
			var entry = DesktopEntryParser.ParseFile(path, Path.GetFileName(path), CultureInfo.CurrentUICulture);
			if (entry is null || string.IsNullOrWhiteSpace(entry.Application.Exec))
				return null;

			// Like Nautilus, only launch .desktop files that are trusted: executable, or installed in an applications directory
			if (!HasExecuteBit(path) && !IsInApplicationsDirectory(path))
			{
				var confirmed = await DialogDisplayHelper.ShowDialogAsync(
					Strings.LinuxUntrustedLauncherTitle.GetLocalizedFormatResource(entry.Application.Name),
					Strings.LinuxUntrustedLauncherText.GetLocalizedResource(),
					Strings.Run.GetLocalizedResource(),
					Strings.Cancel.GetLocalizedResource());

				if (!confirmed)
					return true;
			}

			return await LinuxLauncher.OpenWithAsync(entry.Application, []);
		}

		private static bool IsInApplicationsDirectory(string path)
		{
			var full = Path.GetFullPath(path);
			return XdgDirectories.FromEnvironment().AllDataDirs
				.Select(d => Path.Combine(d, "applications") + Path.DirectorySeparatorChar)
				.Any(d => full.StartsWith(d, StringComparison.Ordinal));
		}

		private static bool HasExecuteBit(string path)
		{
			try
			{
				return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
			{
				return false;
			}
		}

		private enum LinuxExecutableKind
		{
			None,
			Binary,
			Script,
		}

		private static LinuxExecutableKind DetectExecutableKind(string path, string mime)
		{
			try
			{
				Span<byte> head = stackalloc byte[4];
				using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				var read = stream.Read(head);

				if (read >= 4 && head[0] == 0x7F && head[1] == (byte)'E' && head[2] == (byte)'L' && head[3] == (byte)'F')
					return LinuxExecutableKind.Binary;

				if (read >= 2 && head[0] == (byte)'#' && head[1] == (byte)'!')
					return LinuxExecutableKind.Script;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return LinuxExecutableKind.None;
			}

			if (mime is "application/vnd.appimage" or "application/x-executable" or "application/x-pie-executable")
				return LinuxExecutableKind.Binary;

			return LinuxExecutableKind.None;
		}
	}
}
#endif
