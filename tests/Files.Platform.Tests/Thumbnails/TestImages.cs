// Copyright (c) Files Community
// Licensed under the MIT License.

using SkiaSharp;

namespace Files.Platform.Tests.Thumbnails
{
	internal static class TestImages
	{
		/// <summary>
		/// Creates a small solid-color PNG.
		/// </summary>
		public static byte[] CreatePng(int width, int height)
		{
			using var bitmap = new SKBitmap(width, height);
			bitmap.Erase(new SKColor(200, 30, 30));
			using var image = SKImage.FromBitmap(bitmap);
			using var data = image.Encode(SKEncodedImageFormat.Png, 100);
			return data.ToArray();
		}
	}
}
