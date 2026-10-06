// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Files.Platform.Linux.Launching
{
	/// <summary>
	/// A terminal emulator and the arguments needed to open it in a folder or run a command in it.
	/// </summary>
	/// <param name="FileName">The program.</param>
	/// <param name="LeadingArguments">Arguments placed before everything else.</param>
	/// <param name="WorkingDirectoryArguments">Builds the arguments that select the working directory, or null.</param>
	/// <param name="ExecuteArguments">The arguments that introduce a command to run, e.g. <c>-e</c>.</param>
	public sealed record TerminalSpec(
		string FileName,
		IReadOnlyList<string> LeadingArguments,
		Func<string, IReadOnlyList<string>>? WorkingDirectoryArguments,
		IReadOnlyList<string> ExecuteArguments)
	{
		/// <summary>
		/// Builds the argument vector that opens the terminal in <paramref name="folder"/>.
		/// </summary>
		public List<string> BuildOpenArguments(string folder)
		{
			var args = new List<string>(LeadingArguments);
			if (WorkingDirectoryArguments is not null)
				args.AddRange(WorkingDirectoryArguments(folder));

			return args;
		}

		/// <summary>
		/// Builds the argument vector that runs <paramref name="command"/> inside the terminal.
		/// </summary>
		public List<string> BuildExecuteArguments(IReadOnlyList<string> command, string? folder = null)
		{
			var args = folder is null ? new List<string>(LeadingArguments) : BuildOpenArguments(folder);
			args.AddRange(ExecuteArguments);
			args.AddRange(command);
			return args;
		}
	}

	/// <summary>
	/// Detects an installed terminal emulator.
	/// </summary>
	public sealed class TerminalResolver
	{
		private static readonly string[] KnownTerminals =
			["konsole", "gnome-terminal", "kgx", "alacritty", "kitty", "wezterm", "foot", "ghostty", "xterm"];

		private readonly IExecutableLocator locator;
		private readonly Func<string, string?> getEnvironmentVariable;

		/// <summary>
		/// Creates a resolver using the process environment.
		/// </summary>
		public TerminalResolver() : this(new PathExecutableLocator(), Environment.GetEnvironmentVariable)
		{
		}

		/// <summary>
		/// Creates a resolver using the given executable and environment lookups.
		/// </summary>
		public TerminalResolver(IExecutableLocator locator, Func<string, string?> getEnvironmentVariable)
		{
			this.locator = locator;
			this.getEnvironmentVariable = getEnvironmentVariable;
		}

		/// <summary>
		/// Finds a terminal: <c>$TERMINAL</c>, then xdg-terminal-exec, then common terminals. Returns null if none exists.
		/// </summary>
		public TerminalSpec? Resolve()
		{
			var fromEnv = getEnvironmentVariable("TERMINAL");
			if (!string.IsNullOrWhiteSpace(fromEnv))
			{
				var parts = fromEnv.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				if (locator.Locate(parts[0]) is not null)
					return Describe(parts[0], parts.Skip(1).ToArray());
			}

			if (locator.Locate("xdg-terminal-exec") is not null)
				return Describe("xdg-terminal-exec", []);

			foreach (var name in KnownTerminals)
			{
				if (locator.Locate(name) is not null)
					return Describe(name, []);
			}

			return null;
		}

		private static TerminalSpec Describe(string program, string[] extraArguments)
		{
			return Path.GetFileName(program) switch
			{
				"gnome-terminal" => new(program, extraArguments, f => [$"--working-directory={f}"], ["--"]),
				"kgx" => new(program, extraArguments, f => [$"--working-directory={f}"], ["--"]),
				"konsole" => new(program, extraArguments, f => ["--workdir", f], ["-e"]),
				"wezterm" => new(program, ["start", .. extraArguments], f => ["--cwd", f], ["--"]),
				"alacritty" => new(program, extraArguments, f => ["--working-directory", f], ["-e"]),
				"kitty" => new(program, extraArguments, f => ["--directory", f], []),
				"foot" => new(program, extraArguments, f => [$"--working-directory={f}"], []),
				"ghostty" => new(program, extraArguments, f => [$"--working-directory={f}"], ["-e"]),
				"xdg-terminal-exec" => new(program, extraArguments, null, []),
				_ => new(program, extraArguments, null, ["-e"]),
			};
		}
	}
}
