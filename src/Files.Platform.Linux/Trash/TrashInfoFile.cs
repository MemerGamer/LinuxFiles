// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Files.Platform.Linux.Trash
{
	/// <summary>
	/// Reads, writes and encodes <c>.trashinfo</c> files as defined by the FreeDesktop.org Trash specification.
	/// </summary>
	internal static class TrashInfoFile
	{
		public const string Extension = ".trashinfo";

		private const string Header = "[Trash Info]";
		private const string DateFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss";

		public static string Serialize(string path, DateTime localDeletionTime)
		{
			return $"{Header}\nPath={EncodePath(path)}\nDeletionDate={localDeletionTime.ToString(DateFormat, CultureInfo.InvariantCulture)}\n";
		}

		/// <summary>
		/// Parses the file content. Returns <see langword="false"/> when the mandatory <c>Path</c> key is missing or invalid.
		/// </summary>
		public static bool TryParse(string content, out string path, out DateTime? deletionDate)
		{
			path = string.Empty;
			deletionDate = null;

			string? rawPath = null;
			string? rawDate = null;
			var inSection = false;

			foreach (var rawLine in content.Split('\n'))
			{
				var line = rawLine.Trim();
				if (line.StartsWith('['))
				{
					inSection = line == Header;
					continue;
				}

				if (!inSection)
					continue;

				var eq = line.IndexOf('=');
				if (eq <= 0)
					continue;

				var key = line[..eq].Trim();
				var value = line[(eq + 1)..].Trim();
				if (key == "Path")
					rawPath ??= value;
				else if (key == "DeletionDate")
					rawDate ??= value;
			}

			if (string.IsNullOrEmpty(rawPath) || !TryDecodePath(rawPath, out var decoded) || decoded.Length == 0)
				return false;

			path = decoded;

			if (rawDate is not null &&
				DateTime.TryParseExact(rawDate, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
			{
				deletionDate = parsed;
			}

			return true;
		}

		/// <summary>
		/// Percent-encodes the UTF-8 bytes of a path, leaving unreserved characters and <c>/</c> intact.
		/// </summary>
		public static string EncodePath(string path)
		{
			var builder = new StringBuilder(path.Length);
			foreach (var b in Encoding.UTF8.GetBytes(path))
			{
				if (b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or
					(byte)'-' or (byte)'_' or (byte)'.' or (byte)'~' or (byte)'/')
				{
					builder.Append((char)b);
				}
				else
				{
					builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
				}
			}

			return builder.ToString();
		}

		public static bool TryDecodePath(string encoded, out string decoded)
		{
			decoded = string.Empty;
			var bytes = new List<byte>(encoded.Length);

			for (var i = 0; i < encoded.Length; i++)
			{
				var c = encoded[i];
				if (c == '%')
				{
					if (i + 2 >= encoded.Length ||
						!byte.TryParse(encoded.AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
					{
						return false;
					}

					bytes.Add(value);
					i += 2;
				}
				else if (c < 0x80)
				{
					bytes.Add((byte)c);
				}
				else
				{
					var length = char.IsHighSurrogate(c) && i + 1 < encoded.Length ? 2 : 1;
					bytes.AddRange(Encoding.UTF8.GetBytes(encoded.Substring(i, length)));
					i += length - 1;
				}
			}

			try
			{
				decoded = new UTF8Encoding(false, true).GetString(bytes.ToArray());
				return true;
			}
			catch (ArgumentException)
			{
				return false;
			}
		}
	}
}
