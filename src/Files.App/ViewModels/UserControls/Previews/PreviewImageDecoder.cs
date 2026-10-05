// Copyright (c) Files Community
// Licensed under the MIT License.

#if DESKTOP
using System;

namespace Files.App.ViewModels.Previews
{
	public static class PreviewImageDecoder
	{
		private const long MaxImagePixels = 16L * 1000 * 1000;
		private const int MaxDisplayEdge = 2048;
		public static (byte[]? Png, int Width, int Height) DecodeImage(byte[] bytes)
		{
			using var data = SkiaSharp.SKData.CreateCopy(bytes);
			using var codec = SkiaSharp.SKCodec.Create(data);
			if (codec is null)
				return (null, 0, 0);

			var info = codec.Info;
			var origin = codec.EncodedOrigin;
			var swap = origin >= SkiaSharp.SKEncodedOrigin.LeftTop;
			var width = swap ? info.Height : info.Width;
			var height = swap ? info.Width : info.Height;
			if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxImagePixels)
				return (null, width, height);

			// Formats the XAML decoder shows correctly (animated GIF, already upright images of a sane size) keep their original bytes.
			var needsWork = (origin != SkiaSharp.SKEncodedOrigin.TopLeft && origin != SkiaSharp.SKEncodedOrigin.Default)
				|| Math.Max(info.Width, info.Height) > MaxDisplayEdge * 2;
			if (!needsWork)
				return (bytes, width, height);

			using var bitmap = SkiaSharp.SKBitmap.Decode(codec);
			if (bitmap is null)
				return (null, width, height);

			using var upright = ApplyOrigin(bitmap, origin);
			SkiaSharp.SKBitmap target = upright;
			SkiaSharp.SKBitmap? scaled = null;
			var edge = Math.Max(upright.Width, upright.Height);
			if (edge > MaxDisplayEdge)
			{
				var factor = (double)MaxDisplayEdge / edge;
				scaled = upright.Resize(new SkiaSharp.SKImageInfo(Math.Max(1, (int)(upright.Width * factor)), Math.Max(1, (int)(upright.Height * factor))), new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear));
				if (scaled is not null)
					target = scaled;
			}

			using var image = SkiaSharp.SKImage.FromBitmap(target);
			using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
			scaled?.Dispose();

			return (encoded?.ToArray(), width, height);
		}

		private static SkiaSharp.SKBitmap ApplyOrigin(SkiaSharp.SKBitmap source, SkiaSharp.SKEncodedOrigin origin)
		{
			if (origin is SkiaSharp.SKEncodedOrigin.TopLeft or SkiaSharp.SKEncodedOrigin.Default)
				return source.Copy();

			var w = source.Width;
			var h = source.Height;
			var swap = origin >= SkiaSharp.SKEncodedOrigin.LeftTop;
			var result = new SkiaSharp.SKBitmap(swap ? h : w, swap ? w : h);
			using var canvas = new SkiaSharp.SKCanvas(result);
			switch (origin)
			{
				case SkiaSharp.SKEncodedOrigin.TopRight:
					canvas.Translate(w, 0);
					canvas.Scale(-1, 1);
					break;
				case SkiaSharp.SKEncodedOrigin.BottomRight:
					canvas.Translate(w, h);
					canvas.RotateDegrees(180);
					break;
				case SkiaSharp.SKEncodedOrigin.BottomLeft:
					canvas.Translate(0, h);
					canvas.Scale(1, -1);
					break;
				case SkiaSharp.SKEncodedOrigin.LeftTop:
					canvas.Concat(new SkiaSharp.SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1));
					break;
				case SkiaSharp.SKEncodedOrigin.RightTop:
					canvas.Translate(h, 0);
					canvas.RotateDegrees(90);
					break;
				case SkiaSharp.SKEncodedOrigin.RightBottom:
					canvas.Concat(new SkiaSharp.SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1));
					break;
				case SkiaSharp.SKEncodedOrigin.LeftBottom:
					canvas.Translate(0, w);
					canvas.RotateDegrees(270);
					break;
			}
			canvas.DrawBitmap(source, 0, 0);

			return result;
		}
	}
}
#endif
