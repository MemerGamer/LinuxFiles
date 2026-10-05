// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Launching
{
	/// <summary>
	/// Resolves and starts executables without interpreting shell commands.
	/// </summary>
	public interface IExecutableService
	{
		/// <summary>
		/// Finds an executable by path or command name; returns null if unavailable.
		/// </summary>
		string? Locate(string command);

		/// <summary>
		/// Starts an executable with literal arguments; returns false if it cannot be started.
		/// </summary>
		Task<bool> StartAsync(string command, IReadOnlyList<string> arguments);
	}
}
