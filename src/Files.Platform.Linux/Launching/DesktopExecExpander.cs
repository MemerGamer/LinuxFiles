// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Mime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Files.Platform.Linux.Launching
{
	/// <summary>
	/// Turns a Desktop Entry <c>Exec</c> value into argument vectors, applying quoting rules and field codes.
	/// </summary>
	public static class DesktopExecExpander
	{
		private static readonly HashSet<string> Shells = ["sh", "bash", "dash", "zsh", "ksh", "fish", "ash", "csh", "tcsh"];

		/// <summary>
		/// Splits an Exec value into arguments using shell quoting rules (double quotes, single quotes, backslash).
		/// Returns null when quotes are unbalanced.
		/// </summary>
		public static List<string>? Tokenize(string exec)
		{
			var tokens = new List<string>();
			var current = new StringBuilder();
			var hasToken = false;
			var quote = '\0';

			for (var i = 0; i < exec.Length; i++)
			{
				var c = exec[i];
				if (quote == '\'')
				{
					if (c == '\'')
						quote = '\0';
					else
						current.Append(c);
				}
				else if (quote == '"')
				{
					if (c == '\\' && i + 1 < exec.Length && exec[i + 1] is '"' or '`' or '$' or '\\')
						current.Append(exec[++i]);
					else if (c == '"')
						quote = '\0';
					else
						current.Append(c);
				}
				else if (c is '"' or '\'')
				{
					quote = c;
					hasToken = true;
				}
				else if (c == '\\' && i + 1 < exec.Length)
				{
					current.Append(exec[++i]);
					hasToken = true;
				}
				else if (c is ' ' or '\t')
				{
					if (hasToken)
						tokens.Add(current.ToString());
					current.Clear();
					hasToken = false;
				}
				else
				{
					current.Append(c);
					hasToken = true;
				}
			}

			if (quote != '\0')
				return null;

			if (hasToken)
				tokens.Add(current.ToString());

			return tokens;
		}

		/// <summary>
		/// Expands the application's Exec into one or more argument vectors for the given targets (paths or URIs).
		/// Field codes are substituted into the raw Exec string as shell-quoted words before tokenizing, so a value
		/// stays one literal word, including inside <c>sh -c "..."</c> scripts. The first element of each vector is the program.
		/// Returns an empty list when the Exec is empty or has unbalanced quotes.
		/// </summary>
		public static IReadOnlyList<IReadOnlyList<string>> Expand(DesktopApplication application, IReadOnlyList<string> targets)
		{
			var exec = application.Exec;
			if (string.IsNullOrWhiteSpace(exec))
				return [];

			var hasShell = (Tokenize(exec) ?? []).Any(t => Shells.Contains(System.IO.Path.GetFileName(t)));
			var perTarget = HasCode(exec, 'f') || HasCode(exec, 'u');
			IReadOnlyList<string?> singles = !perTarget ? new string?[] { null } : targets.Count == 0 ? new string?[] { string.Empty } : [.. targets];

			var result = new List<IReadOnlyList<string>>();
			foreach (var single in singles)
			{
				var argv = Tokenize(Substitute(application, exec, targets, single, hasShell));
				if (argv is null || argv.Count == 0)
					return [];

				result.Add(argv);
			}

			return result;
		}

		/// <summary>
		/// Quotes a value as a single shell word: wrapped in single quotes with embedded quotes escaped.
		/// </summary>
		public static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

		/// <summary>
		/// Converts a path or file URI to a local path; other URIs are returned unchanged.
		/// </summary>
		public static string ToPathOrUri(string target)
		{
			if (target.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(target, UriKind.Absolute, out var uri))
				return uri.LocalPath;

			return target;
		}

		/// <summary>
		/// Converts a path to a file URI; strings that already are URIs are returned unchanged.
		/// </summary>
		public static string ToUri(string target)
		{
			if (target.Contains("://", StringComparison.Ordinal))
				return target;

			return new Uri(target).AbsoluteUri;
		}

		private static bool HasCode(string exec, char code)
		{
			for (var i = 0; i + 1 < exec.Length; i++)
			{
				if (exec[i] != '%')
					continue;

				if (exec[i + 1] == code)
					return true;

				i++;
			}

			return false;
		}

		private static string Substitute(DesktopApplication app, string exec, IReadOnlyList<string> targets, string? single, bool hasShell)
		{
			var sb = new StringBuilder();
			var quote = '\0';

			for (var i = 0; i < exec.Length; i++)
			{
				var c = exec[i];
				if (c != '%' || i + 1 >= exec.Length)
				{
					if (quote == '\0' && c is '"' or '\'')
						quote = c;
					else if (c == quote)
						quote = '\0';
					else if (c == '\\' && quote != '\'' && i + 1 < exec.Length)
					{
						sb.Append(c).Append(exec[++i]);
						continue;
					}

					sb.Append(c);
					continue;
				}

				var code = exec[++i];
				var value = code switch
				{
					'f' => string.IsNullOrEmpty(single) ? null : Quote(PathArgument(single), quote, hasShell),
					'u' => string.IsNullOrEmpty(single) ? null : Quote(ToUri(single), quote, hasShell),
					'F' => targets.Count == 0 ? null : string.Join(' ', targets.Select(t => Quote(PathArgument(t), quote, hasShell))),
					'U' => targets.Count == 0 ? null : string.Join(' ', targets.Select(t => Quote(ToUri(t), quote, hasShell))),
					'c' => Quote(app.Name, quote, hasShell),
					'k' => Quote(app.DesktopFilePath, quote, hasShell),
					'i' => string.IsNullOrEmpty(app.IconName) ? null : "--icon " + Quote(app.IconName, quote, hasShell),
					_ => null,
				};

				if (code == '%')
					sb.Append('%');
				else if (value is not null)
					sb.Append(value);
			}

			return sb.ToString();
		}

		/// <summary>
		/// Quotes a value for insertion at the current quote context of the Exec string.
		/// </summary>
		private static string Quote(string value, char context, bool hasShell)
		{
			var quoted = ShellQuote(value);
			if (context == '"' && !hasShell)
				quoted = value;
			return context switch
			{
				// Our tokenizer consumes one level of double-quote escapes, so escape for it
				'"' => quoted.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$"),

				// Shells: escape the quoted word for the raw single-quoted script. Others: close the region, insert the word, reopen
				'\'' => hasShell ? quoted.Replace("'", "'\\''") : "'" + quoted + "'",
				_ => quoted,
			};
		}

		/// <summary>
		/// Converts a target to a local path argument; relative paths starting with '-' are prefixed so they cannot be read as options.
		/// </summary>
		private static string PathArgument(string target)
		{
			var path = ToPathOrUri(target);
			return path.StartsWith('-') ? "./" + path : path;
		}
	}
}
