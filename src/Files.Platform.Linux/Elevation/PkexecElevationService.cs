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
		public ElevatedCommand? PlanDelete(string path)
		{
			if (!IsSafeAbsolutePath(path) || IsFilesystemRoot(path))
				return null;

			return Plan("rm", ["-rf", "--", Normalize(path)]);
		}

		/// <inheritdoc/>
		public ElevatedCommand? PlanCopy(string sourcePath, string destinationFolder)
		{
			if (!IsSafeAbsolutePath(sourcePath) || !IsSafeAbsolutePath(destinationFolder) || IsFilesystemRoot(sourcePath))
				return null;

			// -a keeps the source's mode and timestamps; -T is not used so an existing destination folder receives the item
			return Plan("cp", ["-a", "--", Normalize(sourcePath), Normalize(destinationFolder) + "/"]);
		}

		/// <inheritdoc/>
		public ElevatedCommand? PlanMove(string sourcePath, string destinationFolder)
		{
			if (!IsSafeAbsolutePath(sourcePath) || !IsSafeAbsolutePath(destinationFolder) || IsFilesystemRoot(sourcePath))
				return null;

			// -n never replaces an existing item
			return Plan("mv", ["-n", "--", Normalize(sourcePath), Normalize(destinationFolder) + "/"]);
		}

		/// <inheritdoc/>
		public ElevatedCommand? PlanRename(string path, string newName)
		{
			if (!IsSafeAbsolutePath(path) || IsFilesystemRoot(path) || !IsPlainName(newName))
				return null;

			var source = Normalize(path);
			var parent = Path.GetDirectoryName(source) ?? "/";
			return Plan("mv", ["-n", "-T", "--", source, Path.Combine(parent, newName)]);
		}

		/// <inheritdoc/>
		public Task<ElevatedResult> DeleteAsync(string path, CancellationToken cancellationToken = default)
			=> RunAsync(PlanDelete(path), "Refusing to delete this path.", cancellationToken);

		/// <inheritdoc/>
		public Task<ElevatedResult> CopyAsync(string sourcePath, string destinationFolder, CancellationToken cancellationToken = default)
			=> RunAsync(PlanCopy(sourcePath, destinationFolder), "Paths must be absolute.", cancellationToken);

		/// <inheritdoc/>
		public Task<ElevatedResult> MoveAsync(string sourcePath, string destinationFolder, CancellationToken cancellationToken = default)
			=> RunAsync(PlanMove(sourcePath, destinationFolder), "Paths must be absolute.", cancellationToken);

		/// <inheritdoc/>
		public Task<ElevatedResult> RenameAsync(string path, string newName, CancellationToken cancellationToken = default)
			=> RunAsync(PlanRename(path, newName), "Refusing to rename this path.", cancellationToken);

		private ElevatedCommand? Plan(string program, string[] arguments)
			=> locator.Locate(program) is { } target ? new ElevatedCommand(target, arguments) : null;

		private async Task<ElevatedResult> RunAsync(ElevatedCommand? command, string refusal, CancellationToken cancellationToken)
		{
			var pkexec = locator.Locate("pkexec");
			if (pkexec is null)
				return Failure("pkexec is not installed.");

			if (command is null)
				return Failure(refusal);

			var full = new List<string>(command.Arguments.Count + 1) { command.Program };
			full.AddRange(command.Arguments);

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

		private static bool IsPlainName(string name)
			=> !string.IsNullOrEmpty(name) && name is not ("." or "..") && name.IndexOfAny(['/', '\0']) < 0;

		private static ElevatedResult Failure(string message) => new(false, false, -1, message);

		private static bool IsSafeAbsolutePath(string path) => !string.IsNullOrEmpty(path) && path.StartsWith('/') && !path.Contains('\0');

		private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('/') is { Length: > 0 } full ? full : "/";

		private static bool IsFilesystemRoot(string path) => Normalize(path) == "/";
	}
}
