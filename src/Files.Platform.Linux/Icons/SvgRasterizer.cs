// Copyright (c) Files Community
// Licensed under the MIT License.

using SkiaSharp;
using Svg.Skia;
using System;
using System.Collections.Concurrent;
using System.IO;

namespace Files.Platform.Linux.Icons
{
	/// <summary>
	/// Rasterizes SVG icon files to PNG bytes, cached by path and pixel size.
	/// </summary>
	public static class SvgRasterizer
	{
		private static readonly ConcurrentDictionary<(string Path, int Size), byte[]?> _cache = new();

		/// <summary>
		/// Renders the SVG at <paramref name="path"/> into a square PNG of <paramref name="pixelSize"/>, or returns <see langword="null"/> on failure.
		/// </summary>
		public static byte[]? RenderToPng(string path, int pixelSize)
		{
			pixelSize = Math.Clamp(pixelSize, 8, 1024);
			return _cache.GetOrAdd((path, pixelSize), key => Render(key.Path, key.Size));
		}

		/// <summary>
		/// Renders SVG content the caller already read (not cached), so the file is never opened a second time.
		/// </summary>
		public static byte[]? RenderToPng(byte[] svgData, int pixelSize)
		{
			pixelSize = Math.Clamp(pixelSize, 8, 1024);
			using var stream = new System.IO.MemoryStream(svgData, writable: false);
			return Render(svg => svg.Load(stream), pixelSize);
		}

		private static byte[]? Render(string path, int pixelSize)
			=> Render(svg => svg.Load(path), pixelSize);

		private static byte[]? Render(Func<SKSvg, SKPicture?> load, int pixelSize)
		{
			try
			{
				using var svg = new SKSvg();
				svg.Settings.TypefaceProviders = [new CachingTypefaceProvider(svg.Settings.TypefaceProviders)];
				if (load(svg) is not { } picture)
					return null;

				var bounds = picture.CullRect;
				if (bounds.Width <= 0 || bounds.Height <= 0)
					return null;

				var scale = Math.Min(pixelSize / bounds.Width, pixelSize / bounds.Height);
				var info = new SKImageInfo(pixelSize, pixelSize, SKColorType.Rgba8888, SKAlphaType.Premul);
				using var surface = SKSurface.Create(info);
				var canvas = surface.Canvas;
				canvas.Clear(SKColors.Transparent);
				canvas.Translate((pixelSize - bounds.Width * scale) / 2f - bounds.Left * scale, (pixelSize - bounds.Height * scale) / 2f - bounds.Top * scale);
				canvas.Scale(scale);
				canvas.DrawPicture(picture);
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
