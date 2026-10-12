// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Launching
{
	/// <summary>
	/// A process to start detached from the current process.
	/// </summary>
	/// <param name="FileName">The program name or path.</param>
	/// <param name="Arguments">The arguments, without the program.</param>
	/// <param name="WorkingDirectory">The working directory, or null to inherit.</param>
	public sealed record ProcessLaunch(string FileName, IReadOnlyList<string> Arguments, string? WorkingDirectory = null);

	/// <summary>
	/// Starts processes. Exists as a seam so tests never launch real processes.
	/// </summary>
	public interface IProcessStarter
	{
		/// <summary>
		/// Starts the process detached (own session, no inherited stdio). Throws if the program cannot be started.
		/// </summary>
		Task StartDetachedAsync(ProcessLaunch launch, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Starts processes through <c>setsid</c> when available, passing arguments directly without a shell.
	/// </summary>
	public sealed class DetachedProcessStarter : IProcessStarter
	{
		private readonly IExecutableLocator locator;

		/// <summary>
		/// Creates a starter that searches <c>$PATH</c>.
		/// </summary>
		public DetachedProcessStarter() : this(new PathExecutableLocator())
		{
		}

		/// <summary>
		/// Creates a starter using the given executable lookup.
		/// </summary>
		public DetachedProcessStarter(IExecutableLocator locator)
		{
			this.locator = locator;
		}

		/// <inheritdoc/>
		public Task StartDetachedAsync(ProcessLaunch launch, CancellationToken cancellationToken = default)
		{
			var program = locator.Locate(launch.FileName)
				?? throw new FileNotFoundException("Executable not found.", launch.FileName);

			var setsid = locator.Locate("setsid");

			var startInfo = new ProcessStartInfo
			{
				UseShellExecute = false,
				CreateNoWindow = true,
			};

			if (launch.WorkingDirectory is not null)
				startInfo.WorkingDirectory = launch.WorkingDirectory;

			if (setsid is not null)
			{
				startInfo.FileName = setsid;
				startInfo.ArgumentList.Add("--fork");
				startInfo.ArgumentList.Add(program);
			}
			else
			{
				startInfo.FileName = program;
			}

			foreach (var argument in launch.Arguments)
				startInfo.ArgumentList.Add(argument);

			using var process = Files.Platform.Linux.Launching.TracedProcess.Start(startInfo)
				?? throw new InvalidOperationException("The process could not be started.");

			return Task.CompletedTask;
		}
	}

	/// <summary>
	/// Logs launches to stderr instead of starting processes. Enabled with <c>FILES_LAUNCH_DRYRUN=1</c> so automated runs never open real windows.
	/// </summary>
	public sealed class DryRunProcessStarter : IProcessStarter
	{
		/// <inheritdoc/>
		public Task StartDetachedAsync(ProcessLaunch launch, CancellationToken cancellationToken = default)
		{
			Console.Error.WriteLine($"[launch-dryrun] {launch.FileName} {string.Join(' ', launch.Arguments)} (cwd: {launch.WorkingDirectory ?? "-"})");
			return Task.CompletedTask;
		}
	}
}
