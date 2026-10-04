// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Files.Platform.Linux.Icons
{
	/// <summary>
	/// The type of a theme directory as defined by the Icon Theme Specification.
	/// </summary>
	public enum IconDirectoryType
	{
		/// <summary>Fixed size.</summary>
		Fixed,

		/// <summary>Scalable between MinSize and MaxSize.</summary>
		Scalable,

		/// <summary>Matches sizes within Threshold of Size.</summary>
		Threshold,
	}

	/// <summary>
	/// A sub-directory declared in an <c>index.theme</c> file.
	/// </summary>
	public sealed record IconDirectory(string Path, int Size, int Scale, IconDirectoryType Type, int MinSize, int MaxSize, int Threshold)
	{
		/// <summary>
		/// Determines whether the directory can serve an icon of the given size and scale.
		/// </summary>
		public bool Matches(int iconSize, int iconScale)
		{
			if (Scale != iconScale)
				return false;

			return Type switch
			{
				IconDirectoryType.Fixed => Size == iconSize,
				IconDirectoryType.Scalable => MinSize <= iconSize && iconSize <= MaxSize,
				_ => Size - Threshold <= iconSize && iconSize <= Size + Threshold,
			};
		}

		/// <summary>
		/// Gets how far the directory is from the requested size and scale.
		/// </summary>
		public int Distance(int iconSize, int iconScale)
		{
			switch (Type)
			{
				case IconDirectoryType.Fixed:
					return Math.Abs(Size * Scale - iconSize * iconScale);
				case IconDirectoryType.Scalable:
					if (iconSize * iconScale < MinSize * Scale)
						return MinSize * Scale - iconSize * iconScale;
					if (iconSize * iconScale > MaxSize * Scale)
						return iconSize * iconScale - MaxSize * Scale;
					return 0;
				default:
					if (iconSize * iconScale < (Size - Threshold) * Scale)
						return (Size - Threshold) * Scale - iconSize * iconScale;
					if (iconSize * iconScale > (Size + Threshold) * Scale)
						return iconSize * iconScale - (Size + Threshold) * Scale;
					return 0;
			}
		}
	}

	/// <summary>
	/// A parsed <c>index.theme</c> file.
	/// </summary>
	public sealed class IconThemeIndex
	{
		/// <summary>
		/// Gets the names of the themes this theme inherits from.
		/// </summary>
		public IReadOnlyList<string> Inherits { get; init; } = [];

		/// <summary>
		/// Gets the directories declared by the theme.
		/// </summary>
		public IReadOnlyList<IconDirectory> Directories { get; init; } = [];

		/// <summary>
		/// Parses the text of an <c>index.theme</c> file.
		/// </summary>
		public static IconThemeIndex Parse(string content)
		{
			var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
			Dictionary<string, string>? current = null;
			foreach (var raw in content.Split('\n'))
			{
				var line = raw.Trim();
				if (line.Length == 0 || line[0] == '#')
					continue;

				if (line[0] == '[' && line[^1] == ']')
				{
					current = sections[line[1..^1]] = new Dictionary<string, string>(StringComparer.Ordinal);
					continue;
				}

				var eq = line.IndexOf('=');
				if (current is not null && eq > 0)
					current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
			}

			if (!sections.TryGetValue("Icon Theme", out var main))
				return new IconThemeIndex();

			var inherits = Split(main.GetValueOrDefault("Inherits"));
			var names = new List<string>(Split(main.GetValueOrDefault("Directories")));
			names.AddRange(Split(main.GetValueOrDefault("ScaledDirectories")));

			var dirs = new List<IconDirectory>();
			foreach (var name in names)
			{
				if (!sections.TryGetValue(name, out var s) || !TryInt(s, "Size", out var size))
					continue;

				var type = s.GetValueOrDefault("Type") switch
				{
					"Fixed" => IconDirectoryType.Fixed,
					"Scalable" => IconDirectoryType.Scalable,
					_ => IconDirectoryType.Threshold,
				};
				var scale = TryInt(s, "Scale", out var sc) ? sc : 1;
				var min = TryInt(s, "MinSize", out var mn) ? mn : size;
				var max = TryInt(s, "MaxSize", out var mx) ? mx : size;
				var threshold = TryInt(s, "Threshold", out var th) ? th : 2;
				dirs.Add(new IconDirectory(name, size, scale, type, min, max, threshold));
			}

			return new IconThemeIndex { Inherits = inherits, Directories = dirs };
		}

		private static string[] Split(string? value) =>
			value is null ? [] : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		private static bool TryInt(Dictionary<string, string> section, string key, out int value)
		{
			value = 0;
			return section.TryGetValue(key, out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
		}
	}
}
