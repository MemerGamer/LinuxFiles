// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Files.Platform.Abstractions.Archives;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.Xz;
using SharpCompress.Compressors.ZStandard;
using SharpCompress.Readers;

namespace Files.Platform.Linux.Archives
{
	/// <summary>
	/// An entry as read from an archive. Names, sizes and link targets are untrusted.
	/// </summary>
	internal sealed record EntryData(string? Key, bool IsDirectory, long Size, long CompressedSize, DateTime? Modified, bool IsEncrypted, string? LinkTarget, long Crc, int? Attrib, bool IsTar);

	/// <summary>
	/// A readable archive, hiding which SharpCompress API (archive, tar reader, single compressed file) is behind it.
	/// </summary>
	internal abstract class ArchiveSource : IDisposable
	{
		public abstract bool IsSolid { get; }

		/// <summary>Enumerates the entries. Each call starts over. The stream factory is only valid while the enumerator is on that entry.</summary>
		public abstract IEnumerable<(EntryData Entry, Func<Stream> Open)> Entries();

		public virtual void Dispose()
		{
		}

		public static ArchiveSource Open(string path, string? password, Encoding? encoding)
		{
			var options = new ReaderOptions { Password = string.IsNullOrEmpty(password) ? null : password };
			if (encoding is not null)
				options.ArchiveEncoding = new ArchiveEncoding { Forced = encoding };

			var name = Path.GetFileName(path).ToLowerInvariant();
			var codec = TarCodec(name);
			if (codec is not null)
				return new TarSource(path, codec.Value, options);

			var single = SingleFileCodec(name);
			if (single is not null)
				return new SingleFileSource(path, single.Value);

			return new ZipLikeSource(ArchiveFactory.Open(path, options));
		}

		internal static Codec? TarCodec(string lowerName)
		{
			if (lowerName.EndsWith(".tar", StringComparison.Ordinal))
				return Codec.None;
			if (lowerName.EndsWith(".tar.gz", StringComparison.Ordinal) || lowerName.EndsWith(".tgz", StringComparison.Ordinal))
				return Codec.GZip;
			if (lowerName.EndsWith(".tar.bz2", StringComparison.Ordinal) || lowerName.EndsWith(".tbz2", StringComparison.Ordinal) || lowerName.EndsWith(".tbz", StringComparison.Ordinal))
				return Codec.BZip2;
			if (lowerName.EndsWith(".tar.xz", StringComparison.Ordinal) || lowerName.EndsWith(".txz", StringComparison.Ordinal))
				return Codec.Xz;
			if (lowerName.EndsWith(".tar.zst", StringComparison.Ordinal) || lowerName.EndsWith(".tzst", StringComparison.Ordinal))
				return Codec.Zstd;

			return null;
		}

		private static Codec? SingleFileCodec(string lowerName)
		{
			if (lowerName.EndsWith(".gz", StringComparison.Ordinal))
				return Codec.GZip;
			if (lowerName.EndsWith(".bz2", StringComparison.Ordinal))
				return Codec.BZip2;
			if (lowerName.EndsWith(".xz", StringComparison.Ordinal))
				return Codec.Xz;
			if (lowerName.EndsWith(".zst", StringComparison.Ordinal))
				return Codec.Zstd;

			return null;
		}

		internal enum Codec
		{
			None,
			GZip,
			BZip2,
			Xz,
			Zstd,
		}

		internal static Stream Decompress(Stream input, Codec codec) => codec switch
		{
			Codec.None => input,
			Codec.GZip => new GZipStream(input, CompressionMode.Decompress),
			Codec.BZip2 => new BZip2Stream(input, SharpCompress.Compressors.CompressionMode.Decompress, false),
			Codec.Xz => new XZStream(input),
			Codec.Zstd => new DecompressionStream(input),
			_ => throw new ArgumentOutOfRangeException(nameof(codec)),
		};

		private sealed class ZipLikeSource : ArchiveSource
		{
			private readonly IArchive archive;

			public ZipLikeSource(IArchive archive)
			{
				this.archive = archive;
			}

			public override bool IsSolid => archive.IsSolid || archive.Type == ArchiveType.SevenZip;

			public override IEnumerable<(EntryData Entry, Func<Stream> Open)> Entries()
			{
				if (IsSolid)
				{
					using var reader = archive.ExtractAllEntries();
					while (reader.MoveToNextEntry())
					{
						var entry = reader.Entry;
						yield return (Map(entry, false), () => reader.OpenEntryStream());
					}
				}
				else
				{
					foreach (var entry in archive.Entries)
					{
						var current = entry;
						yield return (Map(current, false), () => current.OpenEntryStream());
					}
				}
			}

			public override void Dispose() => archive.Dispose();
		}

		private sealed class TarSource : ArchiveSource
		{
			private readonly string path;
			private readonly Codec codec;
			private readonly ReaderOptions options;

			public TarSource(string path, Codec codec, ReaderOptions options)
			{
				this.path = path;
				this.codec = codec;
				this.options = options;
			}

			public override bool IsSolid => true;

			public override IEnumerable<(EntryData Entry, Func<Stream> Open)> Entries()
			{
				using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
				using var decompressed = Decompress(file, codec);
				using var reader = ReaderFactory.Open(decompressed, options);
				while (reader.MoveToNextEntry())
				{
					var entry = reader.Entry;
					yield return (Map(entry, true), () => reader.OpenEntryStream());
				}
			}
		}

		private sealed class SingleFileSource : ArchiveSource
		{
			private readonly string path;
			private readonly Codec codec;

			public SingleFileSource(string path, Codec codec)
			{
				this.path = path;
				this.codec = codec;
			}

			public override bool IsSolid => false;

			public override IEnumerable<(EntryData Entry, Func<Stream> Open)> Entries()
			{
				var name = Path.GetFileNameWithoutExtension(path);
				var info = new FileInfo(path);
				using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
				using var decompressed = Decompress(file, codec);
				yield return (new EntryData(name, false, 0, info.Length, info.LastWriteTime, false, null, 0, null, false), () => decompressed);
			}
		}

		private static EntryData Map(IEntry entry, bool isTar)
			=> new(entry.Key, entry.IsDirectory, Safe(() => entry.Size, 0), Safe(() => entry.CompressedSize, 0), Safe(() => entry.LastModifiedTime, null),
				Safe(() => entry.IsEncrypted, false), Safe(() => entry.LinkTarget, null), Safe(() => entry.Crc, 0), Safe(() => entry.Attrib, null), isTar);

		// Some SharpCompress entry types do not implement every property
		private static T Safe<T>(Func<T> get, T fallback)
		{
			try
			{
				return get();
			}
			catch (NotImplementedException)
			{
				return fallback;
			}
		}
	}

	/// <summary>
	/// Incremental CRC-32 (IEEE), used to verify entries because SharpCompress does not fail on corrupt stored data.
	/// </summary>
	internal static class Crc32
	{
		private static readonly uint[] Table = Build();

		public static uint Update(uint crc, byte[] buffer, int count)
		{
			crc = ~crc;
			for (var i = 0; i < count; i++)
				crc = Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);

			return ~crc;
		}

		private static uint[] Build()
		{
			var table = new uint[256];
			for (uint i = 0; i < 256; i++)
			{
				var c = i;
				for (var k = 0; k < 8; k++)
					c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;

				table[i] = c;
			}

			return table;
		}
	}
}
