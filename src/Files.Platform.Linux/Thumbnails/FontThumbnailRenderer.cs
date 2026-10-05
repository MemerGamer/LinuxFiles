// Copyright (c) Files Community
// Licensed under the MIT License.

using SkiaSharp;
using System;
using System.IO;

namespace Files.Platform.Linux.Thumbnails
{
	/// <summary>
	/// Renders a sample glyph pair of a font file to a square PNG.
	/// </summary>
	public static class FontThumbnailRenderer
	{
		private const long MaxFontBytes = 64L * 1024 * 1024;

		/// <summary>
		/// Renders "Aa" in the font at <paramref name="path"/>, or returns <see langword="null"/> when the file is not a loadable font.
		/// </summary>
		public static byte[]? RenderToPng(string path, int pixelSize)
		{
			pixelSize = Math.Clamp(pixelSize, 16, 512);
			try
			{
				var info = new FileInfo(path);
				if (!info.Exists || info.Length is 0 or > MaxFontBytes)
					return null;

				using var typeface = SKTypeface.FromFile(path);
				if (typeface is null)
					return null;

				using var font = new SKFont(typeface, pixelSize * 0.55f) { Subpixel = true };
				using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0x8C, 0x8C, 0x8C) };
				const string sample = "Aa";
				var width = font.MeasureText(sample);
				if (width <= 0)
					return null;

				// Shrink glyphs that would overflow the square
				var fit = Math.Min(1f, pixelSize * 0.9f / width);
				font.Size *= fit;
				width *= fit;
				font.GetFontMetrics(out var metrics);

				using var surface = SKSurface.Create(new SKImageInfo(pixelSize, pixelSize, SKColorType.Rgba8888, SKAlphaType.Premul));
				var canvas = surface.Canvas;
				canvas.Clear(SKColors.Transparent);
				var baseline = (pixelSize - (metrics.Descent - metrics.Ascent)) / 2f - metrics.Ascent;
				canvas.DrawText(sample, (pixelSize - width) / 2f, baseline, font, paint);
				canvas.Flush();

				using var image = surface.Snapshot();
				using var data = image.Encode(SKEncodedImageFormat.Png, 100);
				return data.ToArray();
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
