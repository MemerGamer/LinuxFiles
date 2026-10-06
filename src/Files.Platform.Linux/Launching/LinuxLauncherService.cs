// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Launching;
using Files.Platform.Abstractions.Mime;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Launching
{
	/// <summary>
	/// Launches files, URIs, terminals and executables on Linux as detached processes.
	/// </summary>
	public sealed class LinuxLauncherService : ILauncherService
	{
		private readonly IMimeTypeService mimeTypes;
		private readonly IApplicationRegistry applications;
		private readonly IProcessStarter starter;
		private readonly TerminalResolver terminals;
		private readonly RootTerminalResolver rootTerminals;
		private readonly Lazy<TerminalSpec?> rootTerminal;

		/// <summary>
		/// Creates the launcher.
		/// </summary>
		public LinuxLauncherService(IMimeTypeService mimeTypes, IApplicationRegistry applications, IProcessStarter starter, TerminalResolver terminals, RootTerminalResolver? rootTerminals = null)
		{
			this.mimeTypes = mimeTypes;
			this.applications = applications;
			this.starter = starter;
			this.terminals = terminals;
			this.rootTerminals = rootTerminals ?? new RootTerminalResolver();
			rootTerminal = new Lazy<TerminalSpec?>(terminals.Resolve);
		}

		/// <inheritdoc/>
		public async Task<bool> OpenAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
		{
			var groups = new List<(DesktopApplication? App, List<string> Paths)>();
			var refused = false;
			foreach (var path in paths)
			{
				var mime = await mimeTypes.GetMimeTypeAsync(path, cancellationToken).ConfigureAwait(false);

				// Defense in depth: opening is never a way to run something. Executable/launcher types are only run through the
				// confirmation flow (RunCommandAsync / RunExecutableAsync), never through a default handler.
				if (OpenDecision.IsExecutableMimeType(mime))
				{
					refused = true;
					continue;
				}

				var app = await applications.GetDefaultApplicationAsync(mime, cancellationToken).ConfigureAwait(false);

				var group = app is null ? default : groups.FirstOrDefault(g => g.App?.Id == app.Id);
				if (group.Paths is null)
				{
					group = (app, []);
					groups.Add(group);
				}

				group.Paths.Add(path);
			}

			var success = groups.Count > 0 && !refused;
			foreach (var (app, groupPaths) in groups)
			{
				if (app is not null)
				{
					success &= await OpenWithAsync(app, groupPaths, cancellationToken).ConfigureAwait(false);
					continue;
				}

				// No known default application: let xdg-open decide, but never for files with an execute bit, which some
				// handlers would run instead of open
				foreach (var path in groupPaths)
				{
					if (HasExecuteBit(path))
					{
						success = false;
						continue;
					}

					success &= await TryStartAsync(new ProcessLaunch("xdg-open", [path]), cancellationToken).ConfigureAwait(false);
				}
			}

			return success;
		}

		/// <inheritdoc/>
		public async Task<bool> OpenWithAsync(DesktopApplication application, IEnumerable<string> paths, CancellationToken cancellationToken = default)
		{
			var invocations = DesktopExecExpander.Expand(application, paths.ToList());
			if (invocations.Count == 0)
				return false;

			TerminalSpec? terminal = null;
			if (application.RunInTerminal)
			{
				terminal = terminals.Resolve();
				if (terminal is null)
					return false;
			}

			var success = true;
			foreach (var argv in invocations)
			{
				var launch = terminal is null
					? new ProcessLaunch(argv[0], [.. argv.Skip(1)])
					: new ProcessLaunch(terminal.FileName, terminal.BuildExecuteArguments(argv));

				success &= await TryStartAsync(launch, cancellationToken).ConfigureAwait(false);
			}

			return success;
		}

		/// <inheritdoc/>
		public async Task<bool> RunCommandAsync(IReadOnlyList<string> argv, bool inTerminal, CancellationToken cancellationToken = default)
		{
			if (argv.Count == 0)
				return false;

			if (!inTerminal)
				return await TryStartAsync(new ProcessLaunch(argv[0], [.. argv.Skip(1)]), cancellationToken).ConfigureAwait(false);

			var terminal = terminals.Resolve();
			return terminal is not null &&
				await TryStartAsync(new ProcessLaunch(terminal.FileName, terminal.BuildExecuteArguments(argv)), cancellationToken).ConfigureAwait(false);
		}

		private static bool HasExecuteBit(string path)
		{
			try
			{
				if (OperatingSystem.IsWindows())
					return false;

				return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
			{
				return false;
			}
		}

		/// <inheritdoc/>
		public Task<bool> LaunchUriAsync(Uri uri, CancellationToken cancellationToken = default)
		{
			// Local files go through OpenAsync and its gates, never through xdg-open
			if (!uri.IsAbsoluteUri || uri.IsFile)
				return Task.FromResult(false);

			var target = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString;
			return TryStartAsync(new ProcessLaunch("xdg-open", [target]), cancellationToken);
		}

		/// <inheritdoc/>
		public Task<bool> OpenTerminalAsync(string folderPath, CancellationToken cancellationToken = default)
		{
			var terminal = terminals.Resolve();
			if (terminal is null)
				return Task.FromResult(false);

			return TryStartAsync(new ProcessLaunch(terminal.FileName, terminal.BuildOpenArguments(folderPath), folderPath), cancellationToken);
		}

		public bool CanOpenTerminalAsRoot => rootTerminal.Value is not null && rootTerminals.Resolve("/") is not null;

		public Task<bool> OpenTerminalAsRootAsync(string folderPath, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrEmpty(folderPath) || !folderPath.StartsWith('/') || folderPath.StartsWith("//", StringComparison.Ordinal) ||
				folderPath.Contains('\0') || !Directory.Exists(folderPath))
				return Task.FromResult(false);
			var terminal = rootTerminal.Value;
			var command = rootTerminals.Resolve(folderPath);
			if (terminal is null || command is null) return Task.FromResult(false);
			return TryStartAsync(new ProcessLaunch(terminal.FileName, terminal.BuildExecuteArguments(command, folderPath), folderPath), cancellationToken);
		}

		/// <inheritdoc/>
		public Task<bool> RunExecutableAsync(string path, IReadOnlyList<string>? arguments = null, string? workingDirectory = null, CancellationToken cancellationToken = default)
		{
			return TryStartAsync(new ProcessLaunch(path, arguments ?? [], workingDirectory), cancellationToken);
		}

		private async Task<bool> TryStartAsync(ProcessLaunch launch, CancellationToken cancellationToken)
		{
			try
			{
				await starter.StartDetachedAsync(launch, cancellationToken).ConfigureAwait(false);
				return true;
			}
			catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
			{
				return false;
			}
		}
	}
}
