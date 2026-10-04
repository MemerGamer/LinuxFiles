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
		/// <summary>
		/// Splits an Exec value into arguments following the Desktop Entry quoting rules.
		/// </summary>
		public static List<string> Tokenize(string exec)
		{
			var tokens = new List<string>();
			var current = new StringBuilder();
			var hasToken = false;
			var inQuotes = false;

			for (var i = 0; i < exec.Length; i++)
			{
				var c = exec[i];
				if (inQuotes)
				{
					if (c == '\\' && i + 1 < exec.Length && exec[i + 1] is '"' or '`' or '$' or '\\')
					{
						current.Append(exec[++i]);
					}
					else if (c == '"')
					{
						inQuotes = false;
					}
					else
					{
						current.Append(c);
					}
				}
				else if (c == '"')
				{
					inQuotes = true;
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

			if (hasToken)
				tokens.Add(current.ToString());

			return tokens;
		}

		/// <summary>
		/// Expands the application's Exec into one or more argument vectors for the given targets (paths or URIs).
		/// The first element of each vector is the program.
		/// </summary>
		public static IReadOnlyList<IReadOnlyList<string>> Expand(DesktopApplication application, IReadOnlyList<string> targets)
		{
			var tokens = Tokenize(application.Exec);
			if (tokens.Count == 0)
				return [];

			var perTarget = tokens.Any(t => t.Contains("%f", StringComparison.Ordinal) || t.Contains("%u", StringComparison.Ordinal));
			if (!perTarget)
				return [Build(application, tokens, targets, null)];

			if (targets.Count == 0)
				return [Build(application, tokens, targets, string.Empty)];

			return [.. targets.Select(t => Build(application, tokens, targets, t))];
		}

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

		private static List<string> Build(DesktopApplication app, List<string> tokens, IReadOnlyList<string> targets, string? single)
		{
			var result = new List<string>();
			foreach (var token in tokens)
			{
				switch (token)
				{
					case "%F":
						result.AddRange(targets.Select(ToPathOrUri));
						continue;
					case "%U":
						result.AddRange(targets.Select(ToUri));
						continue;
					case "%i":
						if (!string.IsNullOrEmpty(app.IconName))
						{
							result.Add("--icon");
							result.Add(app.IconName);
						}
						continue;
				}

				var expanded = ExpandInline(app, token, single);
				if (expanded.Length > 0 || token.Length == 0)
					result.Add(expanded);
			}

			return result;
		}

		private static string ExpandInline(DesktopApplication app, string token, string? single)
		{
			if (!token.Contains('%'))
				return token;

			var sb = new StringBuilder();
			for (var i = 0; i < token.Length; i++)
			{
				if (token[i] != '%' || i + 1 >= token.Length)
				{
					sb.Append(token[i]);
					continue;
				}

				switch (token[++i])
				{
					case '%': sb.Append('%'); break;
					case 'f': sb.Append(string.IsNullOrEmpty(single) ? string.Empty : ToPathOrUri(single)); break;
					case 'u': sb.Append(string.IsNullOrEmpty(single) ? string.Empty : ToUri(single)); break;
					case 'c': sb.Append(app.Name); break;
					case 'k': sb.Append(app.DesktopFilePath); break;
					case 'i': sb.Append(app.IconName ?? string.Empty); break;
					default: break; // F/U inside a larger argument, and deprecated codes, expand to nothing
				}
			}

			return sb.ToString();
		}
	}
}
