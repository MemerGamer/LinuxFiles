// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Files.Platform.Linux.Launching
{
	/// <summary>
	/// Makes attacker-controlled strings safe to show in confirmation dialogs: control and format characters (bidi overrides,
	/// zero-width characters, newlines) become visible escapes and long values are shortened.
	/// </summary>
	public static class DisplaySanitizer
	{
		/// <summary>The maximum number of characters shown per field.</summary>
		public const int MaxFieldLength = 200;

		/// <summary>The maximum number of argv items shown.</summary>
		public const int MaxArguments = 12;

		/// <summary>
		/// Escapes Cc/Cf characters and line/paragraph separators as visible <c>\uXXXX</c> text.
		/// </summary>
		public static string Escape(string value)
		{
			var sb = new StringBuilder(value.Length);
			foreach (var c in value)
			{
				var category = char.GetUnicodeCategory(c);
				if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
					sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
				else
					sb.Append(c);
			}

			return sb.ToString();
		}

		/// <summary>
		/// Escapes the value, then shortens it to at most <paramref name="maxLength"/> characters with a middle ellipsis.
		/// </summary>
		public static string Field(string? value, int maxLength = MaxFieldLength)
		{
			var escaped = Escape(value ?? string.Empty);
			if (escaped.Length <= maxLength)
				return escaped;

			var keep = (maxLength - 1) / 2;
			return escaped[..keep] + "…" + escaped[^(maxLength - 1 - keep)..];
		}

		/// <summary>
		/// Renders argv one item per line, each sanitized, with at most <see cref="MaxArguments"/> items and a count of the rest.
		/// </summary>
		public static IReadOnlyList<string> Arguments(IReadOnlyList<string> argv)
		{
			var lines = argv.Take(MaxArguments).Select(a => Field(a)).ToList();
			if (argv.Count > MaxArguments)
				lines.Add($"… {argv.Count - MaxArguments} more");

			return lines;
		}
	}
}
