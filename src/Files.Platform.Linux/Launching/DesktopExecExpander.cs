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
		private static readonly HashSet<string> Shells = ["sh", "bash", "dash", "zsh", "ksh", "fish", "ash", "csh", "tcsh", "rbash"];

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
		/// Expands field codes after tokenizing, keeping replacements as argument data.
		/// Shell scripts must be fixed; targets may only be supplied as positional arguments.
		/// </summary>
		public static IReadOnlyList<IReadOnlyList<string>> Expand(DesktopApplication application, IReadOnlyList<string> targets)
		{
			var tokens = Tokenize(application.Exec);
			if (tokens is null || tokens.Count == 0 || tokens[0].Contains('%') || tokens[0].Contains('=')) return [];

			// Quoted field codes have undefined semantics in the desktop entry specification.
			var quote = '\0';
			for (var i = 0; i < application.Exec.Length; i++)
			{
				var c = application.Exec[i];
				if (c == '\\' && quote != '\'' && i + 1 < application.Exec.Length) { i++; continue; }
				if (c == quote) quote = '\0';
				else if (quote == '\0' && c is '\'' or '"') quote = c;
				else if (c == '%' && i + 1 < application.Exec.Length)
				{
					if (application.Exec[++i] != '%' && quote != '\0') return [];
				}
			}

			var targetCodes = 0;
			foreach (var token in tokens)
			{
				for (var i = 0; i < token.Length; i++)
				{
					if (token[i] != '%') continue;
					if (++i == token.Length) return [];
					var code = token[i];
					if (!"%fFuUickdDnNvm".Contains(code)) return [];
					if (code is 'f' or 'F' or 'u' or 'U') targetCodes++;
					if (code is 'F' or 'U' or 'i' && token.Length != 2) return [];
				}
			}
			if (targetCodes > 1) return [];

			// Inspect every interpreter in wrapper chains, but not assignments or shell argv[0].
			var shellNames = new HashSet<int>();
			for (var i = 0; i < tokens.Count; i++)
			{
				if (tokens[i].Contains('=')) continue;
				if (shellNames.Contains(i) && (i + 1 == tokens.Count || !tokens[i + 1].StartsWith('-'))) continue;
				var executable = ExecutableName(tokens[i]);
				if (executable == "env" && tokens.Skip(i + 1).Any(t => t.StartsWith("-S", StringComparison.Ordinal) || t.StartsWith("--split-string", StringComparison.Ordinal))) return [];
				if (!Shells.Contains(executable)) continue;
				var script = -1;
				var commandString = false;
				for (var j = i + 1; j < tokens.Count; j++)
				{
					if (tokens[j] == "--") { script = j + 1; break; }
					if (!tokens[j].StartsWith('-') || tokens[j] == "-") { script = j; break; }
					if (!tokens[j].StartsWith("--", StringComparison.Ordinal) && tokens[j].Contains('c'))
					{
						script = j + 1;
						commandString = true;
						break;
					}
				}
				if (script < 0 || script >= tokens.Count) continue;
				if (tokens.Skip(i).Take(script - i + 1).Any(t => t.Replace("%%", "", StringComparison.Ordinal).Contains('%'))) return [];
				if (commandString) shellNames.Add(script + 1);
			}

			var perTarget = tokens.Any(t => HasCode(t, 'f') || HasCode(t, 'u'));
			IReadOnlyList<string?> singles = !perTarget || targets.Count == 0 ? new string?[] { null } : [.. targets];
			var result = new List<IReadOnlyList<string>>();
			foreach (var single in singles)
			{
				var argv = new List<string>();
				foreach (var token in tokens)
				{
					if (token == "%F") { argv.AddRange(targets.Select(PathArgument)); continue; }
					if (token == "%U") { argv.AddRange(targets.Select(ToUri)); continue; }
					if (token == "%i")
					{
						if (!string.IsNullOrEmpty(application.IconName)) { argv.Add("--icon"); argv.Add(application.IconName); }
						continue;
					}
					var value = new StringBuilder();
					for (var i = 0; i < token.Length; i++)
					{
						if (token[i] != '%') { value.Append(token[i]); continue; }
						value.Append(token[++i] switch
						{
							'%' => "%",
							'f' => single is null ? "" : PathArgument(single),
							'u' => single is null ? "" : ToUri(single),
							'c' => application.Name,
							'k' => application.DesktopFilePath,
							_ => "",
						});
					}
					if (value.Length > 0 || token.Length == 0) argv.Add(value.ToString());
				}
				if (argv.Count == 0 || argv.Any(a => a.Contains('\0'))) return [];
				result.Add(argv.ToArray());
			}
			return result;
		}

		/// <summary>Service menus never invoke a shell. Only a single target may fill a long option's entire value.</summary>
		public static IReadOnlyList<IReadOnlyList<string>> ExpandServiceMenu(DesktopApplication app, IReadOnlyList<string> targets)
		{
			// Refuse shell syntax conservatively, even if quoted. Substituted paths are not inspected as command text.
			if (app.Exec.Any(c => c is '|' or '&' or ';' or '<' or '>' or '`' or '$' or '*' or '?' or '~' or '#' || char.IsControl(c))) return [];
			var tokens = Tokenize(app.Exec);
			if (tokens is null || tokens.Count == 0 || tokens[0].Contains('%') || tokens[0].Contains('=') ||
				tokens.Any(t => Shells.Contains(System.IO.Path.GetFileName(t))) ||
				(System.IO.Path.GetFileName(tokens[0]) == "env" && tokens.Any(t => t.StartsWith("-S", StringComparison.Ordinal) || t.StartsWith("--split-string", StringComparison.Ordinal)))) return [];
			var targetCodes = tokens.Select(t => t is "%f" or "%F" or "%u" or "%U" ? t[1] : LongOptionTargetCode(t)).ToArray();
			if (targetCodes.Count(c => c != '\0') > 1 ||
				(targetCodes.Any(c => c is 'f' or 'F' or 'd' or 'D') && targets.Any(t => !ToPathOrUri(t).StartsWith('/'))) ||
				(tokens.Any(t => LongOptionTargetCode(t) != '\0') && targets.Count != 1)) return [];
			var single = tokens.Any(t => t is "%f" or "%u");
			var result = new List<IReadOnlyList<string>>();
			foreach (var target in single ? targets : new string[] { string.Empty })
			{
				var argv = new List<string>();
				foreach (var token in tokens)
				{
					switch (token)
					{
						case "%f": argv.Add(PathArgument(target)); break;
						case "%u": argv.Add(ToUri(target)); break;
						case "%F": argv.AddRange(targets.Select(PathArgument)); break;
						case "%U": argv.AddRange(targets.Select(ToUri)); break;
						case "%c": argv.Add(app.Name); break;
						case "%k": argv.Add(app.DesktopFilePath); break;
						case "%i":
							if (!string.IsNullOrEmpty(app.IconName)) { argv.Add("--icon"); argv.Add(app.IconName); }
							break;
						default:
							var code = LongOptionTargetCode(token);
							if (code != '\0')
							{
								var value = code is 'u' or 'U' ? ToUri(targets[0]) : PathArgument(targets[0]);
								if (code is 'd' or 'D') value = System.IO.Path.GetDirectoryName(value) ?? "/";
								argv.Add(token[..^2] + value);
								break;
							}
							var literal = new StringBuilder();
							for (var i = 0; i < token.Length; i++)
							{
								if (token[i] != '%') literal.Append(token[i]);
								else if (i + 1 < token.Length && token[++i] == '%') literal.Append('%');
								else return [];
							}
							argv.Add(literal.ToString());
							break;
					}
				}
				if (argv.Count == 0 || argv.Any(a => a.Contains('\0'))) return [];
				result.Add(argv.ToArray());
			}
			return result;
		}

		private static string ExecutableName(string token)
		{
			var name = System.IO.Path.GetFileName(token);
			if (Shells.Contains(name)) return name;
			var path = new PathExecutableLocator().Locate(token);
			return path is null ? name : System.IO.Path.GetFileName(Trash.LinuxTrashService.RealPath(path));
		}

		// Accept only --name=%x with an ASCII option name and no surrounding value text.
		private static char LongOptionTargetCode(string token)
		{
			var equals = token.IndexOf('=');
			if (!token.StartsWith("--", StringComparison.Ordinal) || equals < 3 || equals != token.Length - 3 ||
				token[equals + 1] != '%' || token[^1] is not ('f' or 'F' or 'u' or 'U' or 'd' or 'D') ||
				!char.IsAsciiLetterOrDigit(token[2])) return '\0';
			for (var i = 3; i < equals; i++)
				if (!char.IsAsciiLetterOrDigit(token[i]) && token[i] != '-') return '\0';
			return token[^1];
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
