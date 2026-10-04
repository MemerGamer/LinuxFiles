// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Elevation
{
	/// <summary>
	/// The result of an elevated command.
	/// </summary>
	/// <param name="Succeeded">Whether the command ran and exited with code 0.</param>
	/// <param name="WasDismissed">Whether the user dismissed the authentication prompt or authentication failed.</param>
	/// <param name="ExitCode">The exit code, or -1 if the command did not run.</param>
	/// <param name="Error">The standard error output, if any.</param>
	public sealed record ElevatedResult(bool Succeeded, bool WasDismissed, int ExitCode, string Error);

	/// <summary>
	/// Runs single privileged operations (for example delete or copy into a root-owned folder) after authenticating the user.
	/// </summary>
	public interface IElevationService
	{
		/// <summary>
		/// Gets a value indicating whether elevation is available on this system.
		/// </summary>
		bool IsAvailable { get; }

		/// <summary>
		/// Deletes a file or folder (recursively) as root.
		/// </summary>
		Task<ElevatedResult> DeleteAsync(string path, CancellationToken cancellationToken = default);

		/// <summary>
		/// Copies a file or folder (recursively) into a destination folder as root.
		/// </summary>
		Task<ElevatedResult> CopyAsync(string sourcePath, string destinationFolder, CancellationToken cancellationToken = default);
	}
}
