// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Elevation;
using System;
using System.Collections.Generic;

namespace Files.Platform.Linux.Launching
{
	/// <summary>Builds an interactive elevation command without interpreting file paths as code.</summary>
	public sealed class RootTerminalResolver
	{
		private readonly IExecutableLocator locator;
		private readonly Func<string, string?> environment;
		private readonly Func<bool> disabled;

		public RootTerminalResolver() : this(new PathExecutableLocator(), Environment.GetEnvironmentVariable, () => RootActionsAvailability.IsDisabled) { }

		public RootTerminalResolver(IExecutableLocator locator, Func<string, string?> environment, Func<bool> disabled)
		{
			this.locator = locator;
			this.environment = environment;
			this.disabled = disabled;
		}

		public IReadOnlyList<string>? Resolve(string folder)
		{
			if (disabled()) return null;
			if (locator.Locate("run0") is { } run0) return [run0, "--chdir=" + folder];
			if (locator.Locate("sudo") is { } sudo) return [sudo, "-s"];
			if (locator.Locate("pkexec") is not { } pkexec) return null;
			var shell = environment("SHELL");
			var executable = shell is not null && shell.StartsWith('/') ? locator.Locate(shell) : null;
			executable ??= locator.Locate("/bin/sh");
			return executable is null ? null : [pkexec, "--keep-cwd", executable];
		}
	}
}
