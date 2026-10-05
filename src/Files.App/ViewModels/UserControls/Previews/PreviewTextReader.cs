// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using UtfUnknown;

namespace Files.App.ViewModels.Previews
{
	/// <summary>
	/// Reads the beginning of an untrusted file as text: bounded in size and time, never interpreted or executed.
	/// </summary>
	public static class PreviewTextReader
	{
		/// <summary>
		/// The maximum number of bytes read from a file for a text preview.
		/// </summary>
		public const int MaxBytes = 256 * 1024;

		public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

		public sealed record Result(string Text, bool Truncated, long TotalBytes, string EncodingName, bool LooksBinary);

		public static async Task<Result> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(Timeout);

			var buffer = new byte[MaxBytes + 1];
			var read = 0;
			while (read < buffer.Length)
			{
				var n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), timeout.Token);
				if (n <= 0)
					break;
				read += n;
			}

			var truncated = read > MaxBytes;
			if (truncated)
				read = MaxBytes;

			long total = read + (truncated ? 1 : 0);
			try
			{
				if (stream.CanSeek)
					total = stream.Length;
			}
			catch (NotSupportedException)
			{
			}

			var span = buffer.AsSpan(0, read);
			var looksBinary = span[..Math.Min(read, 8192)].Contains((byte)0) && !HasUnicodeBom(span);

			var (encoding, bomLength) = DetectEncoding(buffer, read);

			// Do not cut a multi-byte sequence in half when the cap lands inside one.
			var decoder = encoding.GetDecoder();
			var chars = new char[encoding.GetMaxCharCount(read - bomLength)];
			var count = decoder.GetChars(buffer, bomLength, read - bomLength, chars, 0, flush: !truncated);
			var text = new string(chars, 0, count);

			return new Result(text, truncated, total, encoding.WebName, looksBinary);
		}

		private static bool HasUnicodeBom(ReadOnlySpan<byte> data)
			=> (data.Length >= 2 && ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF)))
				|| (data.Length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 0xFE && data[3] == 0xFF);

		private static (Encoding Encoding, int BomLength) DetectEncoding(byte[] data, int length)
		{
			if (length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
				return (new UTF8Encoding(false), 3);
			if (length >= 4 && data[0] == 0xFF && data[1] == 0xFE && data[2] == 0 && data[3] == 0)
				return (new UTF32Encoding(false, false), 4);
			if (length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 0xFE && data[3] == 0xFF)
				return (new UTF32Encoding(true, false), 4);
			if (length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
				return (new UnicodeEncoding(false, false), 2);
			if (length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
				return (new UnicodeEncoding(true, false), 2);

			// Plain UTF-8 (including ASCII) is by far the most common case on Linux; only guess for anything else.
			var strict = new UTF8Encoding(false, true);
			try
			{
				_ = strict.GetDecoder().GetCharCount(data, 0, length, flush: false);
				return (new UTF8Encoding(false), 0);
			}
			catch (ArgumentException)
			{
				// Either not UTF-8 or the cap cut a sequence; the detector decides below.
			}

			try
			{
				var detected = CharsetDetector.DetectFromBytes(data, 0, length).Detected;
				if (detected?.Encoding is { } enc && detected.Confidence >= 0.5f)
					return (enc, 0);
			}
			catch (Exception)
			{
			}

			return (new UTF8Encoding(false), 0);
		}
	}
}
