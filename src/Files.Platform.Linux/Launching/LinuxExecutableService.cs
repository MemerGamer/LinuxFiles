// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Launching;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Launching
{
	/// <summary>
	/// Starts user-configured executables directly, with no shell expansion.
	/// </summary>
	public sealed class LinuxExecutableService(IExecutableLocator locator, IProcessStarter starter) : IExecutableService
	{
		/// <inheritdoc/>
		public string? Locate(string command)
		{
			try
			{
				return locator.Locate(command);
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <inheritdoc/>
		public async Task<bool> StartAsync(string command, IReadOnlyList<string> arguments)
		{
			var path = Locate(command);
			if (path is null)
				return false;

			try
			{
				await starter.StartDetachedAsync(new ProcessLaunch(path, arguments)).ConfigureAwait(false);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}
	}

	/// <summary>
	/// Starts the given program using ArgumentList and UseShellExecute=false.
	/// </summary>
	public sealed class DirectProcessStarter : IProcessStarter
	{
		/// <inheritdoc/>
		public Task StartDetachedAsync(ProcessLaunch launch, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var startInfo = new ProcessStartInfo(launch.FileName)
			{
				UseShellExecute = false,
			};
			if (launch.WorkingDirectory is not null)
				startInfo.WorkingDirectory = launch.WorkingDirectory;
			foreach (var argument in launch.Arguments)
				startInfo.ArgumentList.Add(argument);

			using var process = Process.Start(startInfo)
				?? throw new InvalidOperationException("The process could not be started.");
			return Task.CompletedTask;
		}
	}
}
