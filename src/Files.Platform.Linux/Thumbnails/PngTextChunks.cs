// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Files.Platform.Linux.Thumbnails
{
	/// <summary>
	/// Reads and writes PNG <c>tEXt</c> chunks without decoding the image.
	/// </summary>
	public static class PngTextChunks
	{
		private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
		private static readonly uint[] CrcTable = BuildCrcTable();

		/// <summary>
		/// Returns a copy of <paramref name="png"/> with the given Latin-1 text entries inserted after the IHDR chunk.
		/// </summary>
		public static byte[] Insert(byte[] png, IReadOnlyList<KeyValuePair<string, string>> entries)
		{
			if (!HasSignature(png) || png.Length < 33)
				throw new ArgumentException("Not a PNG image.", nameof(png));

			// 8 signature + 25 for IHDR (length, type, 13 data, crc)
			const int afterIhdr = 33;
			using var ms = new System.IO.MemoryStream(png.Length + 256);
			ms.Write(png, 0, afterIhdr);
			foreach (var (key, value) in entries)
			{
				var data = new List<byte>(Encoding.Latin1.GetBytes(key)) { 0 };
				data.AddRange(Encoding.Latin1.GetBytes(value));
				WriteChunk(ms, "tEXt", data.ToArray());
			}

			ms.Write(png, afterIhdr, png.Length - afterIhdr);
			return ms.ToArray();
		}

		/// <summary>
		/// Reads all <c>tEXt</c> entries that precede the image data. Returns an empty dictionary for invalid input.
		/// </summary>
		public static Dictionary<string, string> Read(ReadOnlySpan<byte> png)
		{
			var result = new Dictionary<string, string>(StringComparer.Ordinal);
			if (!HasSignature(png))
				return result;

			var pos = 8;
			while (pos + 12 <= png.Length)
			{
				var length = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(pos, 4));
				if (length > int.MaxValue || pos + 12L + length > png.Length)
					break;

				var type = png.Slice(pos + 4, 4);
				var data = png.Slice(pos + 8, (int)length);
				if (type.SequenceEqual("tEXt"u8))
				{
					var nul = data.IndexOf((byte)0);
					if (nul > 0)
						result[Encoding.Latin1.GetString(data[..nul])] = Encoding.Latin1.GetString(data[(nul + 1)..]);
				}
				else if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8))
				{
					break;
				}

				pos += 12 + (int)length;
			}

			return result;
		}

		/// <summary>
		/// Reads the IHDR dimensions of a PNG without decoding it.
		/// </summary>
		public static bool TryReadDimensions(ReadOnlySpan<byte> png, out int width, out int height)
		{
			width = height = 0;
			if (!HasSignature(png) || png.Length < 33 || !png.Slice(12, 4).SequenceEqual("IHDR"u8))
				return false;

			var w = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(16, 4));
			var h = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(20, 4));
			if (w > int.MaxValue || h > int.MaxValue)
				return false;

			width = (int)w;
			height = (int)h;
			return true;
		}

		private static bool HasSignature(ReadOnlySpan<byte> png) => png.Length >= 8 && png[..8].SequenceEqual(Signature);

		private static void WriteChunk(System.IO.Stream stream, string type, byte[] data)
		{
			Span<byte> buf = stackalloc byte[4];
			BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)data.Length);
			stream.Write(buf);

			var typeBytes = Encoding.ASCII.GetBytes(type);
			stream.Write(typeBytes);
			stream.Write(data);

			var crc = 0xFFFFFFFFu;
			foreach (var b in typeBytes)
				crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
			foreach (var b in data)
				crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);

			BinaryPrimitives.WriteUInt32BigEndian(buf, crc ^ 0xFFFFFFFFu);
			stream.Write(buf);
		}

		private static uint[] BuildCrcTable()
		{
			var table = new uint[256];
			for (uint n = 0; n < 256; n++)
			{
				var c = n;
				for (var k = 0; k < 8; k++)
					c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
				table[n] = c;
			}

			return table;
		}
	}
}
