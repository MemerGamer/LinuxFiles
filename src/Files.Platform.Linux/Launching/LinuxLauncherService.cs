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

		/// <summary>
		/// Creates the launcher.
		/// </summary>
		public LinuxLauncherService(IMimeTypeService mimeTypes, IApplicationRegistry applications, IProcessStarter starter, TerminalResolver terminals)
		{
			this.mimeTypes = mimeTypes;
			this.applications = applications;
			this.starter = starter;
			this.terminals = terminals;
		}

		/// <inheritdoc/>
		public async Task<bool> OpenAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
		{
			var groups = new List<(DesktopApplication? App, List<string> Paths)>();
			foreach (var path in paths)
			{
				var mime = await mimeTypes.GetMimeTypeAsync(path, cancellationToken).ConfigureAwait(false);
				var app = await applications.GetDefaultApplicationAsync(mime, cancellationToken).ConfigureAwait(false);

				var group = app is null ? default : groups.FirstOrDefault(g => g.App?.Id == app.Id);
				if (group.Paths is null)
				{
					group = (app, []);
					groups.Add(group);
				}

				group.Paths.Add(path);
			}

			var success = groups.Count > 0;
			foreach (var (app, groupPaths) in groups)
			{
				if (app is not null)
				{
					success &= await OpenWithAsync(app, groupPaths, cancellationToken).ConfigureAwait(false);
					continue;
				}

				// No known default application: let xdg-open decide
				foreach (var path in groupPaths)
					success &= await TryStartAsync(new ProcessLaunch("xdg-open", [path]), cancellationToken).ConfigureAwait(false);
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
		public Task<bool> LaunchUriAsync(Uri uri, CancellationToken cancellationToken = default)
		{
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
