// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Mime;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Launching
{
	/// <summary>
	/// Launches files, URIs, terminals and executables as detached processes.
	/// </summary>
	public interface ILauncherService
	{
		/// <summary>
		/// Opens paths with their default applications. Returns false if any could not be launched.
		/// </summary>
		Task<bool> OpenAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);

		/// <summary>
		/// Opens paths with a specific application.
		/// </summary>
		Task<bool> OpenWithAsync(DesktopApplication application, IEnumerable<string> paths, CancellationToken cancellationToken = default);

		/// <summary>
		/// Starts exactly the given argument vector (program first), optionally inside a terminal emulator. Nothing is
		/// re-derived or re-expanded, so what a confirmation dialog showed is what runs.
		/// </summary>
		Task<bool> RunCommandAsync(IReadOnlyList<string> argv, bool inTerminal, CancellationToken cancellationToken = default);

		/// <summary>
		/// Opens a URI with the system handler (xdg-open).
		/// </summary>
		Task<bool> LaunchUriAsync(Uri uri, CancellationToken cancellationToken = default);

		/// <summary>
		/// Opens a terminal emulator in a folder. Returns false if no terminal was found.
		/// </summary>
		Task<bool> OpenTerminalAsync(string folderPath, CancellationToken cancellationToken = default);

		/// <summary>Whether an interactive root terminal is available and permitted by the package.</summary>
		bool CanOpenTerminalAsRoot => false;

		/// <summary>Opens a terminal in a folder running an interactive elevation command.</summary>
		Task<bool> OpenTerminalAsRootAsync(string folderPath, CancellationToken cancellationToken = default) => Task.FromResult(false);

		/// <summary>
		/// Starts an executable detached from this process.
		/// </summary>
		Task<bool> RunExecutableAsync(string path, IReadOnlyList<string>? arguments = null, string? workingDirectory = null, CancellationToken cancellationToken = default);
	}
}
