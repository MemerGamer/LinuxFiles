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

			var success = true;
			var plain = new List<(string Path, LinuxOpenPlan Plan)>();
			foreach (var path in list)
			{
				var plan = await PlanAsync(path);
				if (plan.Action == OpenAction.OpenDefault)
					plain.Add((path, plan));
				else
					success &= await ExecutePlanAsync(path, plan);
			}

			// Files could have changed while the dialogs were open: re-verify each one before the shared launch
			var verified = plain.Where(p => p.Plan.StillValid()).Select(p => p.Path).ToList();
			if (verified.Count != plain.Count)
				success = false;

			if (verified.Count > 0)
				success &= await LinuxLauncher.OpenAsync(verified);

			return success;
		}

		/// <summary>
		/// Everything decided about a file before any dialog: the resolved target, its identity, the decision and the
		/// parsed .desktop entry. The same entry object is shown in the dialog and launched.
		/// </summary>
		private sealed record LinuxOpenPlan(OpenAction Action, string Target, FileIdentity? Identity, DesktopEntryParser.Entry? Entry, IReadOnlyList<string>? Argv = null)
		{
			public bool StillValid() => Identity is { } id && id.StillMatches(Target);
		}

		private static async Task<LinuxOpenPlan> PlanAsync(string path)
		{
			var target = ResolveFinalTarget(path);
			var identity = FileIdentity.TryCapture(target);
			if (identity is null)
				return new LinuxOpenPlan(OpenAction.Refuse, target, null, null);

			var mime = await LinuxMimeTypes.GetMimeTypeAsync(path);
			var hasExec = (identity.Value.Mode & 0b001_001_001) != 0;

			// Read the bytes once; the identity must be unchanged afterwards so the bytes belong to that version
			byte[] head;
			DesktopEntryParser.Entry? entry = null;
			IReadOnlyList<string>? argv = null;
			var desktop = DesktopState.None;
			var looksDesktop = mime == "application/x-desktop" || target.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase);
			try
			{
				if (looksDesktop)
				{
					var bytes = await File.ReadAllBytesAsync(target);
					head = bytes.Length > 4 ? bytes[..4] : bytes;
					var lines = System.Text.Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").Split('\n');
					entry = DesktopEntryParser.ParseStrict(lines, target, Path.GetFileName(target), CultureInfo.CurrentUICulture, out var error);
					// The one argv that is displayed and later started; nothing is re-derived after the dialog
					var expanded = entry is null ? [] : DesktopExecExpander.Expand(entry.Application, []);
					argv = expanded.Count == 1 ? expanded[0] : null;
					if (argv is null)
						entry = null;

					desktop = entry is null ? DesktopState.Invalid : DesktopState.Valid;
					if (entry is null)
						App.Logger.LogWarning("Rejected desktop file {Path}: {Error}", target, error);
				}
				else
				{
					head = new byte[4];
					using var stream = new FileStream(target, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite, Options = FileOptions.None });
					var read = await stream.ReadAsync(head);
					head = head[..read];
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return new LinuxOpenPlan(OpenAction.Refuse, target, null, null);
			}

			if (!identity.Value.StillMatches(target))
				return new LinuxOpenPlan(OpenAction.Refuse, target, null, null);

			var action = OpenDecision.Decide(desktop, desktop == DesktopState.Valid && IsInApplicationsDirectory(target), hasExec, OpenDecision.Sniff(head), mime);
			return new LinuxOpenPlan(action, target, identity, entry, argv);
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

			return await ExecutePlanAsync(path, await PlanAsync(path));
		}

		private static async Task<bool> ExecutePlanAsync(string path, LinuxOpenPlan plan)
		{
			var target = plan.Target;
			var workingDirectory = Path.GetDirectoryName(target);

			// The file may have been replaced while a dialog was open (TOCTOU). Residual risk: a swap in the instants between
			// this check and the exec in the launched process, which would need write access to the file's directory.
			async Task<bool> ChangedAsync()
			{
				App.Logger.LogWarning("File changed before it was opened: {Path}", target);
				await DialogDisplayHelper.ShowDialogAsync(Strings.LinuxFileChangedTitle.GetLocalizedResource(), Strings.LinuxFileChangedText.GetLocalizedFormatResource(target));
				return false;
			}

			switch (plan.Action)
			{
				case OpenAction.Refuse:
					await DialogDisplayHelper.ShowDialogAsync(Strings.LinuxOpenRefusedTitle.GetLocalizedResource(), Strings.LinuxOpenRefusedText.GetLocalizedFormatResource(target));
					return false;

				case OpenAction.RunBinaryWithConfirm:
				{
					var confirmed = await DialogDisplayHelper.ShowDialogAsync(
						Strings.LinuxRunExecutableTitle.GetLocalizedFormatResource(Path.GetFileName(target)),
						Strings.LinuxRunBinaryText.GetLocalizedFormatResource(target),
						Strings.Run.GetLocalizedResource(),
						Strings.Cancel.GetLocalizedResource());

					if (!confirmed)
						return true;

					return plan.StillValid() ? await LinuxLauncher.RunExecutableAsync(target, null, workingDirectory) : await ChangedAsync();
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

					var choice = await DialogDisplayHelper.ShowDialogAsync(dialog) switch
					{
						DynamicDialogResult.Primary => ConfirmChoice.Run,
						DynamicDialogResult.Secondary => ConfirmChoice.Display,
						_ => ConfirmChoice.Cancel,
					};

					switch (OpenDecision.Resolve(plan.Action, choice))
					{
						case FollowUp.RunExact:
							return plan.StillValid() ? await LinuxLauncher.RunExecutableAsync(target, null, workingDirectory) : await ChangedAsync();
						case FollowUp.DisplayAsText:
							return plan.StillValid() ? await DisplayAsTextAsync(target) : await ChangedAsync();
						default:
							return true;
					}
				}

				case OpenAction.LaunchDesktopTrusted:
				case OpenAction.LaunchDesktopConfirm:
				{
					var application = plan.Entry!.Application;
					var argv = plan.Argv!;
					if (plan.Action == OpenAction.LaunchDesktopConfirm)
					{
						// Show the exact argv the launcher will receive (control characters made visible)
						var command = string.Join(' ', argv.Select(a => DesktopExecExpander.ShellQuote(MakeVisible(a)))) + (application.RunInTerminal ? " (in a terminal)" : string.Empty);
						var confirmed = await DialogDisplayHelper.ShowDialogAsync(
							Strings.LinuxUntrustedLauncherTitle.GetLocalizedFormatResource(MakeVisible(application.Name)),
							Strings.LinuxUntrustedLauncherText.GetLocalizedFormatResource(command),
							Strings.Run.GetLocalizedResource(),
							Strings.Cancel.GetLocalizedResource());

						if (OpenDecision.Resolve(plan.Action, confirmed ? ConfirmChoice.Run : ConfirmChoice.Cancel) != FollowUp.RunExact)
							return true;
					}

					return plan.StillValid() ? await LinuxLauncher.RunCommandAsync(argv, application.RunInTerminal) : await ChangedAsync();
				}

				default:
					return plan.StillValid() ? await LinuxLauncher.OpenAsync([path]) : await ChangedAsync();
			}
		}

		/// <summary>
		/// Opens a file in the text/plain handler, chosen explicitly: the file's own MIME type could resolve to an interpreter or launcher.
		/// </summary>
		private static async Task<bool> DisplayAsTextAsync(string target)
		{
			var registry = Ioc.Default.GetRequiredService<IApplicationRegistry>();
			var editor = await registry.GetDefaultApplicationAsync("text/plain") ?? (await registry.GetApplicationsForMimeTypeAsync("text/plain")).FirstOrDefault();
			if (editor is null)
			{
				await DialogDisplayHelper.ShowDialogAsync(Strings.LinuxOpenFailedTitle.GetLocalizedResource(), Strings.LinuxOpenFailedText.GetLocalizedFormatResource(target));
				return false;
			}

			return await LinuxLauncher.OpenWithAsync(editor, [target]);
		}

		private static string MakeVisible(string value)
			=> string.Concat(value.Select(c => char.IsControl(c) ? $"\\x{(int)c:X2}" : c.ToString()));

		private static bool IsInApplicationsDirectory(string path)
		{
			var full = Path.GetFullPath(path);
			return XdgDirectories.FromEnvironment().AllDataDirs
				.Select(d => Path.Combine(d, "applications") + Path.DirectorySeparatorChar)
				.Any(d => full.StartsWith(d, StringComparison.Ordinal));
		}
	}
}
#endif
