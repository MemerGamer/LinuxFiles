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
	/// A fully resolved privileged command: the program and its argument vector. Nothing is ever passed through a shell.
	/// </summary>
	/// <param name="Program">The absolute path of the program run as root.</param>
	/// <param name="Arguments">The arguments, exactly as passed.</param>
	public sealed record ElevatedCommand(string Program, IReadOnlyList<string> Arguments)
	{
		/// <summary>Gets the command as shown to the user, with the privilege helper in front.</summary>
		public string DisplayText => "pkexec " + Program + " " + string.Join(' ', Arguments);
	}

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

		/// <summary>
		/// Moves a file or folder into a destination folder as root.
		/// </summary>
		Task<ElevatedResult> MoveAsync(string sourcePath, string destinationFolder, CancellationToken cancellationToken = default);

		/// <summary>
		/// Renames a file or folder in place as root. <paramref name="newName"/> is a name, never a path, and an existing item is never replaced.
		/// </summary>
		Task<ElevatedResult> RenameAsync(string path, string newName, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets the exact command a delete would run, or null if the path is refused. Used to show the user what will happen.
		/// </summary>
		ElevatedCommand? PlanDelete(string path);

		/// <summary>Gets the exact command a copy would run, or null if refused.</summary>
		ElevatedCommand? PlanCopy(string sourcePath, string destinationFolder);

		/// <summary>Gets the exact command a move would run, or null if refused.</summary>
		ElevatedCommand? PlanMove(string sourcePath, string destinationFolder);

		/// <summary>Gets the exact command a rename would run, or null if refused.</summary>
		ElevatedCommand? PlanRename(string path, string newName);
	}
}
