// Copyright (c) Files Community
// Licensed under the MIT License.

using SkiaSharp;
using Svg.Skia;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;

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

		private const long MaxSvgBytes = 4L * 1024 * 1024;

		// Breeze-style "translate(-384.57-515.8)" omits the separator between numbers, which Svg.Skia drops silently
		private static readonly Regex TransformAttribute = new("(?<=\\btransform\\s*=\\s*\"[^\"]*)(?<=\\d)(?=-)", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		/// <summary>
		/// Inserts the missing comma between adjacent numbers inside <c>transform</c> attributes.
		/// </summary>
		public static string NormalizeTransforms(string svg) => TransformAttribute.Replace(svg, ",");

		private static byte[]? Render(string path, int pixelSize)
		{
			try
			{
				using var svg = new SKSvg();
				var fileInfo = new FileInfo(path);
				if (!fileInfo.Exists || fileInfo.Length > MaxSvgBytes)
					return null;

				if (svg.FromSvg(NormalizeTransforms(File.ReadAllText(path))) is not { } picture)
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
