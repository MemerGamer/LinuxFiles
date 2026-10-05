// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Mime;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Files.Platform.Linux.Mime
{
	/// <summary>
	/// Resolves MIME types using the shared-mime-info database (globs2 and mime XML) with a small magic-number fallback.
	/// </summary>
	public sealed class LinuxMimeTypeService : IMimeTypeService
	{
		private const string DirectoryType = "inode/directory";
		private const string SymlinkType = "inode/symlink";
		private const string ZeroSizeType = "application/x-zerosize";
		private const string OctetStreamType = "application/octet-stream";
		private const string TextType = "text/plain";
		private const int SniffLength = 512;

		private static readonly XNamespace MimeNs = "http://www.freedesktop.org/standards/shared-mime-info";
		private static readonly XNamespace XmlNs = XNamespace.Xml;

		private readonly XdgDirectories xdg;
		private readonly CultureInfo culture;
		private readonly Lazy<GlobIndex> globs;
		private readonly ConcurrentDictionary<string, MimeXmlEntry> mimeXmlCache = new(StringComparer.Ordinal);


		private sealed record MimeXmlEntry(XElement? Root, string? File, DateTime LastWriteUtc, long CheckedAt);

		private sealed record GlobEntry(int Order, int Weight, string MimeType, string Pattern, string LowerPattern, bool CaseSensitive, bool IsLiteral)
		{
			/// <summary>Gets whether this entry outranks <paramref name="other"/>: higher weight, then longer pattern, then earlier in the database.</summary>
			public bool Beats(GlobEntry? other) =>
				other is null || Weight > other.Weight ||
				(Weight == other.Weight && (Pattern.Length > other.Pattern.Length || (Pattern.Length == other.Pattern.Length && Order < other.Order)));
		}

		/// <summary>
		/// Globs grouped so a lookup touches only plausible entries: exact names and plain "*suffix" patterns are hashed, the rest are scanned.
		/// </summary>
		private sealed class GlobIndex
		{
			private readonly Dictionary<string, List<GlobEntry>> literalsCs = new(StringComparer.Ordinal);
			private readonly Dictionary<string, List<GlobEntry>> literalsCi = new(StringComparer.Ordinal);
			private readonly Dictionary<string, List<GlobEntry>> suffixesCs = new(StringComparer.Ordinal);
			private readonly Dictionary<string, List<GlobEntry>> suffixesCi = new(StringComparer.Ordinal);
			private readonly List<GlobEntry> complex = [];

			public GlobIndex(IEnumerable<GlobEntry> entries)
			{
				foreach (var entry in entries)
				{
					if (entry.IsLiteral)
					{
						Add(entry.CaseSensitive ? literalsCs : literalsCi, entry.CaseSensitive ? entry.Pattern : entry.LowerPattern, entry);
					}
					else if (entry.Pattern.Length > 1 && entry.Pattern[0] == '*' && entry.Pattern.AsSpan(1).IndexOfAny("*?[\\") < 0)
					{
						Add(entry.CaseSensitive ? suffixesCs : suffixesCi, entry.CaseSensitive ? entry.Pattern[1..] : entry.LowerPattern[1..], entry);
					}
					else
					{
						complex.Add(entry);
					}
				}
			}

			private static void Add(Dictionary<string, List<GlobEntry>> map, string key, GlobEntry entry)
			{
				if (!map.TryGetValue(key, out var list))
					map[key] = list = [];
				list.Add(entry);
			}

			public string? Match(string fileName, string lower)
			{
				GlobEntry? best = null;

				void Consider(Dictionary<string, List<GlobEntry>> map, ReadOnlySpan<char> key)
				{
					if (map.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(key, out var list))
					{
						foreach (var entry in list)
						{
							if (entry.Beats(best))
								best = entry;
						}
					}
				}

				Consider(literalsCs, fileName);
				Consider(literalsCi, lower);
				for (var i = 0; i < fileName.Length; i++)
				{
					Consider(suffixesCs, fileName.AsSpan(i));
					Consider(suffixesCi, lower.AsSpan(i));
				}

				foreach (var glob in complex)
				{
					if (!glob.Beats(best))
						continue;

					var matches = glob.CaseSensitive ? GlobMatch(glob.Pattern, fileName) : GlobMatch(glob.LowerPattern, lower);
					if (matches)
						best = glob;
				}

				return best?.MimeType;
			}
		}

		/// <summary>
		/// Creates the service for the process environment and current UI culture.
		/// </summary>
		public LinuxMimeTypeService() : this(XdgDirectories.FromEnvironment(), CultureInfo.CurrentUICulture)
		{
		}

		/// <summary>
		/// Creates the service for explicit XDG directories and culture.
		/// </summary>
		public LinuxMimeTypeService(XdgDirectories xdg, CultureInfo culture)
		{
			this.xdg = xdg;
			this.culture = culture;
			globs = new(() => new GlobIndex(LoadGlobs()));
		}

		/// <summary>
		/// Gets how long a cached MIME XML (or a miss) is trusted before the file is checked again, so newly installed types appear without a restart.
		/// </summary>
		public TimeSpan RecheckInterval { get; init; } = TimeSpan.FromSeconds(5);

		/// <inheritdoc/>
		public async Task<string> GetMimeTypeAsync(string path, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (Directory.Exists(path))
				return DirectoryType;

			var byName = MatchGlobs(Path.GetFileName(path.TrimEnd('/')));
			if (IsDanglingSymbolicLink(path))
				return SymlinkType;

			if (!File.Exists(path))
				return byName ?? OctetStreamType;

			if (byName is not null)
				return byName;

			return await SniffAsync(path, cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public Task<string?> GetDescriptionAsync(string mimeType, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var root = LoadMimeXml(mimeType);
			if (root is null)
				return Task.FromResult<string?>(null);

			var comments = root.Elements(MimeNs + "comment").ToList();
			var tag = culture.Name.Replace('-', '_');
			string? lang = tag.Contains('_') ? tag[..tag.IndexOf('_')] : tag;

			string? Find(string? wanted) => comments
				.FirstOrDefault(c => (string?)c.Attribute(XmlNs + "lang") == wanted)?.Value.Trim();

			var result = (tag.Length > 0 ? Find(tag) : null) ?? (lang.Length > 0 ? Find(lang) : null) ?? Find(null);
			return Task.FromResult(result);
		}

		/// <inheritdoc/>
		public Task<string> GetIconNameAsync(string mimeType, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var explicitIcon = (string?)LoadMimeXml(mimeType)?.Element(MimeNs + "icon")?.Attribute("name");
			return Task.FromResult(explicitIcon ?? mimeType.Replace('/', '-'));
		}

		/// <inheritdoc/>
		public Task<string> GetGenericIconNameAsync(string mimeType, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var explicitIcon = (string?)LoadMimeXml(mimeType)?.Element(MimeNs + "generic-icon")?.Attribute("name");
			if (explicitIcon is not null)
				return Task.FromResult(explicitIcon);

			var slash = mimeType.IndexOf('/');
			var media = slash > 0 ? mimeType[..slash] : mimeType;
			return Task.FromResult(media == "inode" && mimeType == DirectoryType ? "folder" : $"{media}-x-generic");
		}

		/// <summary>
		/// Matches a file name against the glob database. Returns null when nothing matches.
		/// Highest weight wins; ties are broken by the longest pattern.
		/// </summary>
		private string? MatchGlobs(string fileName)
		{
			if (fileName.Length == 0)
				return null;

			return globs.Value.Match(fileName, fileName.ToLowerInvariant());
		}

		/// <summary>
		/// Matches a name against a glob supporting <c>*</c>, <c>?</c> and <c>[...]</c>.
		/// </summary>
		public static bool GlobMatch(string pattern, string name)
		{
			int p = 0, n = 0, starP = -1, starN = 0;
			while (n < name.Length)
			{
				if (p < pattern.Length && pattern[p] == '*')
				{
					starP = p++;
					starN = n;
				}
				else if (p < pattern.Length && TryMatchSingle(pattern, ref p, name[n]))
				{
					n++;
				}
				else if (starP >= 0)
				{
					p = starP + 1;
					n = ++starN;
				}
				else
				{
					return false;
				}
			}

			while (p < pattern.Length && pattern[p] == '*')
				p++;

			return p == pattern.Length;
		}

		private static bool TryMatchSingle(string pattern, ref int p, char c)
		{
			switch (pattern[p])
			{
				case '?':
					p++;
					return true;
				case '[':
				{
					var end = pattern.IndexOf(']', p + 2 <= pattern.Length ? p + 2 : p + 1);
					if (end < 0)
					{
						// Unterminated class matches a literal '['
						if (c != '[')
							return false;
						p++;
						return true;
					}

					var set = pattern.AsSpan(p + 1, end - p - 1);
					var negate = set.Length > 0 && set[0] is '!' or '^';
					if (negate)
						set = set[1..];

					var matched = false;
					for (var i = 0; i < set.Length; i++)
					{
						if (i + 2 < set.Length && set[i + 1] == '-')
						{
							matched |= c >= set[i] && c <= set[i + 2];
							i += 2;
						}
						else
						{
							matched |= set[i] == c;
						}
					}

					if (matched == negate)
						return false;

					p = end + 1;
					return true;
				}
				case '\\' when p + 1 < pattern.Length:
					if (pattern[p + 1] != c)
						return false;
					p += 2;
					return true;
				default:
					if (pattern[p] != c)
						return false;
					p++;
					return true;
			}
		}

		private GlobEntry[] LoadGlobs()
		{
			var entries = new List<GlobEntry>();
			foreach (var dir in xdg.AllDataDirs)
			{
				var globs2 = Path.Combine(dir, "mime", "globs2");
				if (TryReadLines(globs2, out var lines))
				{
					foreach (var line in lines)
					{
						if (line.Length == 0 || line[0] == '#')
							continue;

						// weight:mime:glob[:flags]
						var parts = line.Split(':', 4);
						if (parts.Length < 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var weight))
							continue;

						var flags = parts.Length > 3 ? parts[3].Split(',') : [];
						entries.Add(MakeEntry(entries.Count, weight, parts[1], parts[2], flags.Contains("cs")));
					}
				}
				else if (TryReadLines(Path.Combine(dir, "mime", "globs"), out lines))
				{
					foreach (var line in lines)
					{
						if (line.Length == 0 || line[0] == '#')
							continue;

						var parts = line.Split(':', 2);
						if (parts.Length == 2)
							entries.Add(MakeEntry(entries.Count, 50, parts[0], parts[1], false));
					}
				}
			}

			return [.. entries];
		}

		private static GlobEntry MakeEntry(int order, int weight, string mime, string pattern, bool caseSensitive) =>
			new(order, weight, mime, pattern, pattern.ToLowerInvariant(), caseSensitive, pattern.IndexOfAny(['*', '?', '[']) < 0);

		private static bool TryReadLines(string path, out string[] lines)
		{
			try
			{
				lines = File.ReadAllLines(path);
				return true;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				lines = [];
				return false;
			}
		}

		private XElement? LoadMimeXml(string mimeType)
		{
			var now = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
			if (mimeXmlCache.TryGetValue(mimeType, out var entry))
			{
				if (now - entry.CheckedAt < RecheckInterval.Ticks)
					return entry.Root;

				// Keep the entry while the highest-priority file is the same and unchanged (a new override in an earlier directory, or a type
				// installed after a miss, changes which file that is)
				var current = FindMimeXmlPath(mimeType);
				if (current == entry.File && (current is null || TryGetWriteTime(current) == entry.LastWriteUtc))
				{
					mimeXmlCache[mimeType] = entry with { CheckedAt = now };
					return entry.Root;
				}
			}

			var (root, file) = ReadMimeXml(mimeType);
			file = FindMimeXmlPath(mimeType) ?? file;
			mimeXmlCache[mimeType] = new MimeXmlEntry(root, file, file is null ? default : TryGetWriteTime(file), now);
			return root;
		}

		private static DateTime TryGetWriteTime(string file)
		{
			try
			{
				return File.GetLastWriteTimeUtc(file);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return default;
			}
		}

		private string? FindMimeXmlPath(string mimeType)
		{
			if (mimeType.Contains("..", StringComparison.Ordinal) || mimeType.Split('/').Length != 2)
				return null;

			foreach (var dir in xdg.AllDataDirs)
			{
				var file = Path.Combine(dir, "mime", mimeType + ".xml");
				if (File.Exists(file))
					return file;
			}

			return null;
		}

		private (XElement? Root, string? File) ReadMimeXml(string mimeType)
		{
			if (mimeType.Contains("..", StringComparison.Ordinal) || mimeType.Split('/').Length != 2)
				return (null, null);

			foreach (var dir in xdg.AllDataDirs)
			{
				var file = Path.Combine(dir, "mime", mimeType + ".xml");
				if (!File.Exists(file))
					continue;

				try
				{
					return (XDocument.Load(file).Root, file);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
				{
				}
			}

			return (null, null);
		}

		private static bool IsDanglingSymbolicLink(string path)
		{
			try
			{
				var info = new FileInfo(path);
				return info.LinkTarget is not null && info.ResolveLinkTarget(true)?.Exists != true;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return false;
			}
		}

		private static async Task<string> SniffAsync(string path, CancellationToken cancellationToken)
		{
			byte[] buffer = new byte[SniffLength];
			int read;
			try
			{
				if (new FileInfo(path).Length == 0)
					return ZeroSizeType;

				await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous);
				read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return OctetStreamType;
			}

			return SniffBuffer(buffer.AsSpan(0, read));
		}

		/// <summary>
		/// Detects a few common types from the leading bytes of a file.
		/// </summary>
		public static string SniffBuffer(ReadOnlySpan<byte> data)
		{
			if (data.Length == 0)
				return ZeroSizeType;

			if (data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
				return "image/png";
			if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
				return "image/jpeg";
			if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))
				return "image/gif";
			if (data.StartsWith("%PDF-"u8))
				return "application/pdf";
			if (data.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x03, 0x04]))
				return "application/zip";
			if (data.StartsWith((ReadOnlySpan<byte>)[0x1F, 0x8B]))
				return "application/gzip";
			if (data.StartsWith("BZh"u8))
				return "application/x-bzip2";
			if (data.StartsWith((ReadOnlySpan<byte>)[0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00]))
				return "application/x-xz";
			if (data.StartsWith((ReadOnlySpan<byte>)[0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]))
				return "application/x-7z-compressed";
			if (data.StartsWith((ReadOnlySpan<byte>)[0x7F, 0x45, 0x4C, 0x46]))
				return "application/x-executable";
			if (data.StartsWith("#!"u8))
				return "text/x-shellscript";

			return LooksLikeText(data) ? TextType : OctetStreamType;
		}

		private static bool LooksLikeText(ReadOnlySpan<byte> data)
		{
			foreach (var b in data)
			{
				if (b == 0 || (b < 0x20 && b is not (byte)'\t' and not (byte)'\n' and not (byte)'\r' and not (byte)'\f' and not 0x1b))
					return false;
			}

			// A truncated final multi-byte sequence is acceptable
			var text = data;
			for (var trim = 0; trim < 4 && trim < data.Length; trim++)
			{
				text = data[..(data.Length - trim)];
				if (System.Text.Unicode.Utf8.IsValid(text))
					return true;
			}

			return false;
		}
	}
}
