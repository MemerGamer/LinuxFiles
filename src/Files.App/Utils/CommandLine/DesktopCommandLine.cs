// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App.Utils.CommandLine
{
	/// <summary>
	/// The result of parsing a Linux command line (the arguments after the program name).
	/// </summary>
	/// <param name="Paths">Absolute paths to open.</param>
	/// <param name="Selects">Absolute paths to show with the item selected (<c>--select</c>).</param>
	/// <param name="NewTab">Whether a new tab was asked for (<c>-t</c>, <c>--new-tab</c>).</param>
	/// <param name="NewWindow">Whether a new window was asked for (<c>-n</c>, <c>--new-window</c>).</param>
	public sealed record DesktopLaunchOptions(IReadOnlyList<string> Paths, IReadOnlyList<string> Selects, bool NewTab, bool NewWindow)
	{
		/// <summary>
		/// Gets a value indicating whether the command line asks for anything besides showing the window.
		/// </summary>
		public bool IsEmpty => Paths.Count == 0 && Selects.Count == 0;

		/// <summary>
		/// Converts the options to the commands <c>MainWindow</c> already knows how to execute.
		/// </summary>
		public ParsedCommands ToParsedCommands()
		{
			var commands = new ParsedCommands();

			foreach (var path in Paths)
			{
				var command = new ParsedCommand { Type = ParsedCommandType.OpenPath };
				command.Args.Add(path);
				commands.Add(command);
			}

			foreach (var path in Selects)
			{
				var command = new ParsedCommand { Type = ParsedCommandType.SelectItem };
				command.Args.Add(path);
				commands.Add(command);
			}

			return commands;
		}
	}

	/// <summary>
	/// Parses the command line on Linux: <c>files [-t] [-n] [--select PATH]... [--] [PATH|file:// URI]...</c>.
	/// Unlike the Windows parser, the program name is not part of the arguments and relative paths are resolved against the launch directory.
	/// </summary>
	public static class DesktopCommandLine
	{
		/// <summary>
		/// Parses <paramref name="args"/>. Unknown options are ignored; a missing <c>--select</c> value is ignored.
		/// </summary>
		public static DesktopLaunchOptions Parse(IReadOnlyList<string> args, string workingDirectory)
		{
			var paths = new List<string>();
			var selects = new List<string>();
			var newTab = false;
			var newWindow = false;
			var optionsEnded = false;

			for (var i = 0; i < args.Count; i++)
			{
				var arg = args[i];

				if (!optionsEnded && arg == "--")
				{
					optionsEnded = true;
				}
				else if (!optionsEnded && (arg is "-t" or "--new-tab"))
				{
					newTab = true;
				}
				else if (!optionsEnded && (arg is "-n" or "--new-window"))
				{
					newWindow = true;
				}
				else if (!optionsEnded && arg == "--select")
				{
					if (i + 1 < args.Count && Resolve(args[++i], workingDirectory) is { } selected)
						selects.Add(selected);
				}
				else if (!optionsEnded && arg.StartsWith("--select=", StringComparison.Ordinal))
				{
					if (Resolve(arg["--select=".Length..], workingDirectory) is { } selected)
						selects.Add(selected);
				}
				else if (!optionsEnded && arg.StartsWith('-') && arg.Length > 1)
				{
					// Unknown option (for example one the desktop shell adds): ignore it
				}
				else if (Resolve(arg, workingDirectory) is { } path)
				{
					paths.Add(path);
				}
			}

			return new DesktopLaunchOptions(paths, selects, newTab, newWindow);
		}

		private static string? Resolve(string value, string workingDirectory)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
				return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile ? Normalize(uri.LocalPath) : null;

			try
			{
				return Normalize(Path.GetFullPath(value, string.IsNullOrEmpty(workingDirectory) ? Environment.CurrentDirectory : workingDirectory));
			}
			catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
			{
				return null;
			}
		}

		private static string Normalize(string path)
			=> path.Length > 1 ? path.TrimEnd('/') is { Length: > 0 } trimmed ? trimmed : "/" : path;
	}
}
