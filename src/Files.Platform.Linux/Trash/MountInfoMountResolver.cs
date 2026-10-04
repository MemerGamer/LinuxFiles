// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Files.Platform.Linux.Trash
{
	/// <summary>
	/// Resolves mount points by parsing <c>/proc/self/mountinfo</c>.
	/// </summary>
	public sealed class MountInfoMountResolver : IMountResolver
	{
		private const string DefaultMountInfoPath = "/proc/self/mountinfo";

		private readonly Func<IEnumerable<string>> _readLines;

		/// <summary>
		/// Initializes a resolver that reads the system mount table.
		/// </summary>
		public MountInfoMountResolver()
			: this(() => File.ReadLines(DefaultMountInfoPath))
		{
		}

		/// <summary>
		/// Initializes a resolver that reads mountinfo-formatted lines from <paramref name="readLines"/>.
		/// </summary>
		public MountInfoMountResolver(Func<IEnumerable<string>> readLines)
		{
			_readLines = readLines;
		}

		/// <inheritdoc/>
		public IReadOnlyList<MountEntry> GetMounts()
		{
			var result = new List<MountEntry>();

			try
			{
				foreach (var line in _readLines())
				{
					if (TryParseLine(line, out var entry))
						result.Add(entry);
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}

			return result;
		}

		/// <inheritdoc/>
		public string GetMountPoint(string realPath)
		{
			var best = "/";

			foreach (var mount in GetMounts())
			{
				if (mount.MountPoint.Length > best.Length && IsUnder(realPath, mount.MountPoint))
					best = mount.MountPoint;
			}

			return best;
		}

		/// <summary>
		/// Parses one mountinfo line.
		/// </summary>
		public static bool TryParseLine(string line, out MountEntry entry)
		{
			entry = null!;

			var fields = line.Split(' ');
			var separator = Array.IndexOf(fields, "-");
			if (fields.Length < 7 || separator < 6 || separator + 1 >= fields.Length)
				return false;

			var mountPoint = Unescape(fields[4]);
			if (!mountPoint.StartsWith('/'))
				return false;

			entry = new MountEntry(mountPoint, fields[separator + 1]);
			return true;
		}

		/// <summary>
		/// Determines whether <paramref name="path"/> equals or is located below <paramref name="directory"/>.
		/// </summary>
		public static bool IsUnder(string path, string directory)
		{
			if (directory == "/")
				return true;

			return path.StartsWith(directory, StringComparison.Ordinal) &&
				(path.Length == directory.Length || path[directory.Length] == '/');
		}

		private static string Unescape(string value)
		{
			if (!value.Contains('\\'))
				return value;

			var builder = new StringBuilder(value.Length);
			var pending = new List<byte>();
			for (var i = 0; i < value.Length; i++)
			{
				if (value[i] == '\\' && i + 3 < value.Length && IsOctal(value[i + 1]) && IsOctal(value[i + 2]) && IsOctal(value[i + 3]))
				{
					pending.Add((byte)Convert.ToInt32(value.Substring(i + 1, 3), 8));
					i += 3;
					continue;
				}

				Flush(builder, pending);
				builder.Append(value[i]);
			}

			Flush(builder, pending);
			return builder.ToString();
		}

		private static void Flush(StringBuilder builder, List<byte> pending)
		{
			if (pending.Count == 0)
				return;

			builder.Append(Encoding.UTF8.GetString(pending.ToArray()));
			pending.Clear();
		}

		private static bool IsOctal(char c) => c is >= '0' and <= '7';
	}
}
