// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;

namespace Files.Platform.Linux.Mime
{
	/// <summary>
	/// A parsed mimeapps.list file.
	/// </summary>
	public sealed class MimeAppsList
	{
		/// <summary>The <c>[Default Applications]</c> associations.</summary>
		public Dictionary<string, List<string>> Defaults { get; } = new(StringComparer.Ordinal);

		/// <summary>The <c>[Added Associations]</c> associations.</summary>
		public Dictionary<string, List<string>> Added { get; } = new(StringComparer.Ordinal);

		/// <summary>The <c>[Removed Associations]</c> associations.</summary>
		public Dictionary<string, List<string>> Removed { get; } = new(StringComparer.Ordinal);

		/// <summary>
		/// Parses a mimeapps.list file. Returns null when it cannot be read.
		/// </summary>
		public static MimeAppsList? Load(string path)
		{
			string[] lines;
			try
			{
				lines = File.ReadAllLines(path);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return null;
			}

			var result = new MimeAppsList();
			Dictionary<string, List<string>>? section = null;
			foreach (var raw in lines)
			{
				var line = raw.Trim();
				if (line.Length == 0 || line[0] == '#')
					continue;

				if (line[0] == '[')
				{
					section = line switch
					{
						"[Default Applications]" => result.Defaults,
						"[Added Associations]" => result.Added,
						"[Removed Associations]" => result.Removed,
						_ => null,
					};
					continue;
				}

				var eq = line.IndexOf('=');
				if (section is null || eq <= 0)
					continue;

				var mime = line[..eq].Trim();
				var ids = DesktopEntryParser.SplitList(line[(eq + 1)..].Trim());
				if (!section.TryGetValue(mime, out var list))
					section[mime] = list = [];

				foreach (var id in ids)
				{
					if (!list.Contains(id))
						list.Add(id);
				}
			}

			return result;
		}

		/// <summary>
		/// Rewrites a mimeapps.list so <paramref name="desktopId"/> is the first default for <paramref name="mimeType"/>,
		/// preserving unrelated lines and removing the app from <c>[Removed Associations]</c>.
		/// </summary>
		public static List<string> SetDefault(IReadOnlyList<string> existingLines, string mimeType, string desktopId)
		{
			var lines = new List<string>(existingLines);
			var key = mimeType + "=";

			(int Start, int End) FindSection(string header)
			{
				var start = lines.FindIndex(l => l.Trim() == header);
				if (start < 0)
					return (-1, -1);

				var end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith('['));
				return (start, end < 0 ? lines.Count : end);
			}

			var removed = FindSection("[Removed Associations]");
			if (removed.Start >= 0)
			{
				for (var i = removed.End - 1; i > removed.Start; i--)
				{
					if (!lines[i].TrimStart().StartsWith(key, StringComparison.Ordinal))
						continue;

					var rest = Array.FindAll(DesktopEntryParser.SplitList(lines[i].Trim()[key.Length..]), x => x != desktopId);
					if (rest.Length == 0)
						lines.RemoveAt(i);
					else
						lines[i] = key + string.Join(';', rest) + ";";
				}
			}

			var defaults = FindSection("[Default Applications]");
			if (defaults.Start < 0)
			{
				if (lines.Count > 0 && lines[^1].Length > 0)
					lines.Add(string.Empty);

				lines.Add("[Default Applications]");
				lines.Add($"{key}{desktopId};");
				return lines;
			}

			for (var i = defaults.Start + 1; i < defaults.End; i++)
			{
				if (!lines[i].TrimStart().StartsWith(key, StringComparison.Ordinal))
					continue;

				var others = Array.FindAll(DesktopEntryParser.SplitList(lines[i].Trim()[key.Length..]), x => x != desktopId);
				lines[i] = $"{key}{desktopId};" + (others.Length > 0 ? string.Join(';', others) + ";" : string.Empty);
				return lines;
			}

			var insertAt = defaults.End;
			while (insertAt > defaults.Start + 1 && lines[insertAt - 1].Trim().Length == 0)
				insertAt--;

			lines.Insert(insertAt, $"{key}{desktopId};");
			return lines;
		}
	}
}
