// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Clipboard;
using System;
using System.Collections.Generic;
using System.Text;

namespace Files.Platform.Linux.Clipboard
{
	/// <summary>
	/// Serializes and parses the file-list formats file managers exchange through the clipboard and XDND:
	/// <c>text/uri-list</c> (RFC 2483), <c>x-special/gnome-copied-files</c> (GNOME, GTK, Cinnamon, MATE, Thunar, PCManFM)
	/// and <c>application/x-kde-cutselection</c> (KDE).
	/// </summary>
	public static class ClipboardFormats
	{
		public const string UriList = "text/uri-list";

		public const string GnomeCopiedFiles = "x-special/gnome-copied-files";

		public const string KdeCutSelection = "application/x-kde-cutselection";

		public const string Utf8String = "UTF8_STRING";

		public const string PlainTextUtf8 = "text/plain;charset=utf-8";

		public const string PlainText = "text/plain";

		/// <summary>Upper bound on the number of entries read from an untrusted clipboard, to bound memory and the work a paste can queue.</summary>
		public const int MaxItems = 100_000;

		private static readonly UTF8Encoding Utf8 = new(false);

		/// <summary>
		/// Gets the targets offered for a file list, most specific first.
		/// </summary>
		public static IReadOnlyList<string> GetTargets(ClipboardOperation operation)
		{
			var targets = new List<string> { GnomeCopiedFiles, UriList };
			if (operation == ClipboardOperation.Cut)
				targets.Add(KdeCutSelection);
			targets.Add(Utf8String);
			targets.Add(PlainTextUtf8);
			targets.Add(PlainText);
			return targets;
		}

		/// <summary>
		/// Renders the bytes served for <paramref name="target"/>, or <see langword="null"/> when the target is not offered.
		/// </summary>
		public static byte[]? Render(string target, IReadOnlyList<string> paths, ClipboardOperation operation) => target switch
		{
			GnomeCopiedFiles => Utf8.GetBytes(BuildGnomeCopiedFiles(paths, operation)),
			UriList => Utf8.GetBytes(BuildUriList(paths)),
			KdeCutSelection when operation == ClipboardOperation.Cut => Utf8.GetBytes("1"),
			Utf8String or PlainTextUtf8 or PlainText => Utf8.GetBytes(BuildPlainText(paths)),
			_ => null,
		};

		/// <summary>
		/// Builds the <c>text/uri-list</c> body: one <c>file://</c> URI per line, CRLF terminated.
		/// </summary>
		public static string BuildUriList(IReadOnlyList<string> paths)
		{
			var builder = new StringBuilder();
			foreach (var path in paths)
				builder.Append(PathToUri(path)).Append("\r\n");

			return builder.ToString();
		}

		/// <summary>
		/// Builds the <c>x-special/gnome-copied-files</c> body: the operation (<c>copy</c> or <c>cut</c>) followed by one URI per line, with no trailing newline.
		/// </summary>
		public static string BuildGnomeCopiedFiles(IReadOnlyList<string> paths, ClipboardOperation operation)
		{
			var builder = new StringBuilder(operation == ClipboardOperation.Cut ? "cut" : "copy");
			foreach (var path in paths)
				builder.Append('\n').Append(PathToUri(path));

			return builder.ToString();
		}

		/// <summary>
		/// Builds the plain text offered for pasting into text fields: one path per line.
		/// </summary>
		public static string BuildPlainText(IReadOnlyList<string> paths) => string.Join('\n', paths);

		/// <summary>
		/// Converts an absolute path to a <c>file://</c> URI. Everything but RFC 3986 unreserved characters and <c>/</c> is percent-encoded (UTF-8), so
		/// spaces, <c>#</c>, <c>?</c>, <c>%</c> and non-ASCII names survive a round trip.
		/// </summary>
		public static string PathToUri(string path)
		{
			var bytes = Utf8.GetBytes(path);
			var builder = new StringBuilder("file://", 7 + bytes.Length + 8);
			foreach (var b in bytes)
			{
				if (b is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9')
					or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~' or (byte)'/')
				{
					builder.Append((char)b);
				}
				else
				{
					builder.Append('%').Append("0123456789ABCDEF"[b >> 4]).Append("0123456789ABCDEF"[b & 0xF]);
				}
			}

			return builder.ToString();
		}

		/// <summary>
		/// Converts a <c>file://</c> URI to an absolute local path. Returns <see langword="null"/> for other schemes, remote hosts, relative paths,
		/// malformed escapes and paths containing NUL.
		/// </summary>
		public static string? UriToPath(string uri)
		{
			uri = uri.Trim();
			if (!uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
				return null;

			var rest = uri.AsSpan(5);
			if (!rest.StartsWith("//"))
				return null;

			rest = rest[2..];
			var slash = rest.IndexOf('/');
			if (slash < 0)
				return null;

			var host = rest[..slash];
			if (host.Length != 0 && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
				return null;

			rest = rest[slash..];

			// Fragments and queries are not part of a local file URI produced by file managers
			var cut = rest.IndexOfAny('?', '#');
			if (cut >= 0)
				rest = rest[..cut];

			var bytes = new byte[rest.Length * 3];
			var length = 0;
			for (var i = 0; i < rest.Length; i++)
			{
				var c = rest[i];
				if (c == '%')
				{
					if (i + 2 >= rest.Length)
						return null;

					var hi = HexValue(rest[i + 1]);
					var lo = HexValue(rest[i + 2]);
					if (hi < 0 || lo < 0)
						return null;

					bytes[length++] = (byte)(hi << 4 | lo);
					i += 2;
				}
				else if (c < 0x80)
				{
					bytes[length++] = (byte)c;
				}
				else
				{
					// Non-ASCII characters that were not percent-encoded: keep them as UTF-8
					var units = char.IsHighSurrogate(c) && i + 1 < rest.Length && char.IsLowSurrogate(rest[i + 1]) ? 2 : 1;
					length += Utf8.GetBytes(rest.Slice(i, units), bytes.AsSpan(length));
					i += units - 1;
				}
			}

			if (Array.IndexOf(bytes, (byte)0, 0, length) >= 0)
				return null;

			var path = Utf8.GetString(bytes, 0, length);
			return path.Length > 0 && path[0] == '/' ? path : null;
		}

		/// <summary>
		/// Parses a <c>text/uri-list</c> body. Comments (<c>#</c>), blank lines and non-local or malformed URIs are skipped.
		/// </summary>
		public static IReadOnlyList<string> ParseUriList(string text)
		{
			var paths = new List<string>();
			foreach (var raw in text.Split('\n'))
			{
				var line = raw.TrimEnd('\r');
				if (line.Length == 0 || line[0] == '#')
					continue;

				if (UriToPath(line) is { } path)
				{
					paths.Add(path);
					if (paths.Count >= MaxItems)
						break;
				}
			}

			return paths;
		}

		/// <summary>
		/// Parses a <c>x-special/gnome-copied-files</c> body. Returns <see langword="null"/> when the first line is not a known operation or no usable URI follows.
		/// </summary>
		public static ClipboardFileList? ParseGnomeCopiedFiles(string text)
		{
			var newline = text.IndexOf('\n');
			if (newline < 0)
				return null;

			var operation = text[..newline].Trim() switch
			{
				"copy" => ClipboardOperation.Copy,
				"cut" => ClipboardOperation.Cut,
				_ => (ClipboardOperation?)null,
			};

			if (operation is null)
				return null;

			var paths = ParseUriList(text[(newline + 1)..]);
			return paths.Count == 0 ? null : new ClipboardFileList(paths, operation.Value);
		}

		/// <summary>
		/// Combines the targets of a foreign clipboard owner into a file list, preferring <c>x-special/gnome-copied-files</c> and then
		/// <c>text/uri-list</c> with the KDE cut marker.
		/// </summary>
		/// <param name="gnomeCopiedFiles">The data of <see cref="GnomeCopiedFiles"/>, if offered.</param>
		/// <param name="uriList">The data of <see cref="UriList"/>, if offered.</param>
		/// <param name="kdeCutSelection">The data of <see cref="KdeCutSelection"/>, if offered.</param>
		public static ClipboardFileList? Combine(byte[]? gnomeCopiedFiles, byte[]? uriList, byte[]? kdeCutSelection)
		{
			if (gnomeCopiedFiles is not null &&
				ParseGnomeCopiedFiles(Utf8.GetString(gnomeCopiedFiles)) is { } gnome)
			{
				return gnome;
			}

			if (uriList is null)
				return null;

			var paths = ParseUriList(Utf8.GetString(uriList));
			if (paths.Count == 0)
				return null;

			var isCut = kdeCutSelection is { Length: > 0 } && kdeCutSelection[0] == (byte)'1';
			return new ClipboardFileList(paths, isCut ? ClipboardOperation.Cut : ClipboardOperation.Copy);
		}

		private static int HexValue(char c) => c switch
		{
			>= '0' and <= '9' => c - '0',
			>= 'a' and <= 'f' => c - 'a' + 10,
			>= 'A' and <= 'F' => c - 'A' + 10,
			_ => -1,
		};
	}
}
