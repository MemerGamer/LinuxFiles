// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Elevation;
using Files.Platform.Linux.Launching;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Elevation
{
	/// <summary>
	/// Runs a program and reports how it ended. Exists as a seam so tests never start the real <c>pkexec</c>.
	/// </summary>
	public interface IElevatedProcessRunner
	{
		/// <summary>
		/// Runs <paramref name="fileName"/> with <paramref name="arguments"/> (no shell) and waits for it.
		/// </summary>
		Task<(int ExitCode, string StandardError)> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
	}

	/// <summary>
	/// Starts real processes.
	/// </summary>
	public sealed class ProcessElevatedRunner : IElevatedProcessRunner
	{
		/// <inheritdoc/>
		public async Task<(int ExitCode, string StandardError)> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
		{
			var startInfo = new ProcessStartInfo(fileName)
			{
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				RedirectStandardInput = true,
				CreateNoWindow = true,
			};

			foreach (var argument in arguments)
				startInfo.ArgumentList.Add(argument);

			using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The process could not be started.");
			process.StandardInput.Close();
			var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
			var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

			try
			{
				await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
				throw;
			}

			await stdout.ConfigureAwait(false);
			return (process.ExitCode, await stderr.ConfigureAwait(false));
		}
	}

	/// <summary>
	/// Runs single privileged operations through <c>pkexec</c> (polkit shows the authentication dialog).
	/// Only fixed, argument-vector commands are run (<c>rm</c>, <c>cp</c>); paths are never interpolated into a shell.
	/// </summary>
	public sealed class PkexecElevationService : IElevationService
	{
		// pkexec: 126 = the user dismissed the dialog or is not authorized, 127 = authentication failed or could not be started
		private const int PkexecNotAuthorized = 126;
		private const int PkexecAuthFailed = 127;

		private readonly IExecutableLocator locator;
		private readonly IElevatedProcessRunner runner;

		/// <summary>
		/// Creates the service.
		/// </summary>
		public PkexecElevationService(IExecutableLocator locator, IElevatedProcessRunner runner)
		{
			this.locator = locator;
			this.runner = runner;
		}

		/// <inheritdoc/>
		public bool IsAvailable => locator.Locate("pkexec") is not null;

		/// <inheritdoc/>
		public Task<ElevatedResult> DeleteAsync(string path, CancellationToken cancellationToken = default)
		{
			if (!IsSafeAbsolutePath(path) || IsFilesystemRoot(path))
				return Task.FromResult(Failure("Refusing to delete this path."));

			return RunPrivilegedAsync("rm", ["-rf", "--", Normalize(path)], cancellationToken);
		}

		/// <inheritdoc/>
		public Task<ElevatedResult> CopyAsync(string sourcePath, string destinationFolder, CancellationToken cancellationToken = default)
		{
			if (!IsSafeAbsolutePath(sourcePath) || !IsSafeAbsolutePath(destinationFolder))
				return Task.FromResult(Failure("Paths must be absolute."));

			// -a keeps the source's mode and timestamps; -T is not used so an existing destination folder receives the item
			return RunPrivilegedAsync("cp", ["-a", "--", Normalize(sourcePath), Normalize(destinationFolder) + "/"], cancellationToken);
		}

		private async Task<ElevatedResult> RunPrivilegedAsync(string program, string[] arguments, CancellationToken cancellationToken)
		{
			var pkexec = locator.Locate("pkexec");
			if (pkexec is null)
				return Failure("pkexec is not installed.");

			var target = locator.Locate(program);
			if (target is null)
				return Failure($"{program} was not found.");

			var full = new List<string>(arguments.Length + 1) { target };
			full.AddRange(arguments);

			try
			{
				var (exitCode, error) = await runner.RunAsync(pkexec, full, cancellationToken).ConfigureAwait(false);
				var dismissed = exitCode is PkexecNotAuthorized or PkexecAuthFailed;
				return new ElevatedResult(exitCode == 0, dismissed, exitCode, error);
			}
			catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
			{
				return Failure(ex.Message);
			}
		}

		private static ElevatedResult Failure(string message) => new(false, false, -1, message);

		private static bool IsSafeAbsolutePath(string path) => !string.IsNullOrEmpty(path) && path.StartsWith('/') && !path.Contains('\0');

		private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('/') is { Length: > 0 } full ? full : "/";

		private static bool IsFilesystemRoot(string path) => Normalize(path) == "/";
	}
}
