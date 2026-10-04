// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Files.App.Dialogs;
using Files.Platform.Abstractions.Launching;
using Files.Platform.Abstractions.Mime;
using Files.Platform.Linux.Launching;
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

				var opened = await OpenFileLinuxAsync(path, openViaApplicationPicker);
				if (!opened)
					await DialogDisplayHelper.ShowDialogAsync(Strings.LinuxOpenFailedTitle.GetLocalizedResource(), Strings.LinuxOpenFailedText.GetLocalizedFormatResource(path));

				return opened;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				App.Logger.LogWarning(ex, "Failed to open {Path}", path);
				return false;
			}
		}

		/// <summary>
		/// Opens several files. Files that need a gate (executables, launchers) are opened one by one through
		/// <see cref="OpenFileLinuxAsync"/>; plain documents are passed to their default applications together.
		/// </summary>
		private static async Task<bool> OpenFilesLinuxAsync(IEnumerable<string> paths)
		{
			var list = paths.ToList();

			// Opening many files at once is easy to do by accident: ask first
			if (list.Count > Constants.Actions.MaxSelectedItems)
			{
				var confirmed = await DialogDisplayHelper.ShowDialogAsync(
					Strings.LinuxOpenManyTitle.GetLocalizedFormatResource(list.Count),
					Strings.LinuxOpenManyText.GetLocalizedResource(),
					Strings.Open.GetLocalizedResource(),
					Strings.Cancel.GetLocalizedResource());

				if (!confirmed)
					return true;
			}

			var plain = new List<string>();
			var success = true;
			foreach (var path in list)
			{
				var (action, _) = await ClassifyAsync(path);
				if (action == OpenAction.OpenDefault)
					plain.Add(path);
				else
					success &= await OpenFileLinuxAsync(path);
			}

			if (plain.Count > 0)
				success &= await LinuxLauncher.OpenAsync(plain);

			return success;
		}

		private static async Task<(OpenAction Action, ExecutableKind Kind)> ClassifyAsync(string path)
		{
			var target = ResolveFinalTarget(path);
			var mime = await LinuxMimeTypes.GetMimeTypeAsync(target);
			var isDesktop = (mime == "application/x-desktop" || target.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)) &&
				DesktopEntryParser.ParseFile(target, Path.GetFileName(target), CultureInfo.CurrentUICulture) is { } e &&
				!string.IsNullOrWhiteSpace(e.Application.Exec);

			var hasExec = HasExecuteBit(target);
			var kind = hasExec && !isDesktop ? DetectExecutableKind(target, mime) : ExecutableKind.None;
			return (OpenDecision.Decide(isDesktop, isDesktop && IsInApplicationsDirectory(target), hasExec, kind), kind);
		}

		/// <summary>
		/// Follows symlinks so decisions are made on what would actually run.
		/// </summary>
		private static string ResolveFinalTarget(string path)
		{
			try
			{
				var info = new FileInfo(path);
				if (info.LinkTarget is not null && info.ResolveLinkTarget(true) is { } resolved)
					return resolved.FullName;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}

			return path;
		}

		internal static async Task<bool> OpenFileLinuxAsync(string path, bool openViaApplicationPicker = false)
		{
			if (openViaApplicationPicker)
				return await LinuxOpenWithDialog.ShowAsync(path);

			var (action, _) = await ClassifyAsync(path);
			var target = ResolveFinalTarget(path);
			var workingDirectory = Path.GetDirectoryName(target);

			switch (action)
			{
				case OpenAction.RunBinaryWithConfirm:
				{
					var confirmed = await DialogDisplayHelper.ShowDialogAsync(
						Strings.LinuxRunExecutableTitle.GetLocalizedFormatResource(Path.GetFileName(target)),
						Strings.LinuxRunBinaryText.GetLocalizedFormatResource(target),
						Strings.Run.GetLocalizedResource(),
						Strings.Cancel.GetLocalizedResource());

					return !confirmed || await LinuxLauncher.RunExecutableAsync(target, null, workingDirectory);
				}

				case OpenAction.RunScriptWithConfirm:
				{
					var dialog = new DynamicDialog(new DynamicDialogViewModel()
					{
						TitleText = Strings.LinuxRunExecutableTitle.GetLocalizedFormatResource(Path.GetFileName(target)),
						SubtitleText = Strings.LinuxRunExecutableText.GetLocalizedFormatResource(target),
						PrimaryButtonText = Strings.Run.GetLocalizedResource(),
						SecondaryButtonText = Strings.LinuxDisplayFile.GetLocalizedResource(),
						CloseButtonText = Strings.Cancel.GetLocalizedResource(),
						DynamicButtons = DynamicDialogButtons.Primary | DynamicDialogButtons.Secondary | DynamicDialogButtons.Cancel
					});

					switch (await DialogDisplayHelper.ShowDialogAsync(dialog))
					{
						case DynamicDialogResult.Primary:
							return await LinuxLauncher.RunExecutableAsync(target, null, workingDirectory);
						case DynamicDialogResult.Secondary:
							return await LinuxLauncher.OpenAsync([path]);
						default:
							return true;
					}
				}

				case OpenAction.LaunchDesktopTrusted:
				case OpenAction.LaunchDesktopConfirm:
				{
					var entry = DesktopEntryParser.ParseFile(target, Path.GetFileName(target), CultureInfo.CurrentUICulture)!;
					if (action == OpenAction.LaunchDesktopConfirm)
					{
						// The execute bit is not trust: always show exactly what would run
						var confirmed = await DialogDisplayHelper.ShowDialogAsync(
							Strings.LinuxUntrustedLauncherTitle.GetLocalizedFormatResource(entry.Application.Name),
							Strings.LinuxUntrustedLauncherText.GetLocalizedFormatResource(entry.Application.Exec),
							Strings.Run.GetLocalizedResource(),
							Strings.Cancel.GetLocalizedResource());

						if (!confirmed)
							return true;
					}

					return await LinuxLauncher.OpenWithAsync(entry.Application, []);
				}

				default:
					return await LinuxLauncher.OpenAsync([path]);
			}
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

		private static ExecutableKind DetectExecutableKind(string path, string mime)
		{
			try
			{
				Span<byte> head = stackalloc byte[4];
				using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				var read = stream.Read(head);

				if (read >= 4 && head[0] == 0x7F && head[1] == (byte)'E' && head[2] == (byte)'L' && head[3] == (byte)'F')
					return ExecutableKind.Binary;

				if (read >= 2 && head[0] == (byte)'#' && head[1] == (byte)'!')
					return ExecutableKind.Script;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return ExecutableKind.None;
			}

			if (mime is "application/vnd.appimage" or "application/x-executable" or "application/x-pie-executable")
				return ExecutableKind.Binary;

			return ExecutableKind.None;
		}
	}
}
#endif
