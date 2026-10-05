// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Thumbnails;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;

namespace Files.Platform.Tests.Thumbnails
{
	[TestClass]
	public sealed class FontThumbnailRendererTests
	{
		[TestMethod]
		public void ReturnsNullForNonFont()
		{
			var path = Path.Combine(Path.GetTempPath(), "files-font-" + Guid.NewGuid().ToString("N") + ".ttf");
			File.WriteAllText(path, "not a font");
			try
			{
				Assert.IsNull(FontThumbnailRenderer.RenderToPng(path, 64));
			}
			finally
			{
				File.Delete(path);
			}
		}

		[TestMethod]
		public void RefusesUntrustedFontEvenWhenValid()
		{
			var system = new[] { "/usr/share/fonts" }.Where(Directory.Exists)
				.SelectMany(d => Directory.EnumerateFiles(d, "*.ttf", SearchOption.AllDirectories).Take(1)).FirstOrDefault();
			if (system is null)
				Assert.Inconclusive("No system font installed");
			var copy = Path.Combine(Path.GetTempPath(), "files-font-" + Guid.NewGuid().ToString("N") + ".ttf");
			File.Copy(system, copy);
			try
			{
				Assert.IsFalse(FontThumbnailRenderer.IsSystemInstalledFontPath(copy));
				Assert.IsNull(FontThumbnailRenderer.RenderToPng(copy, 64));
			}
			finally
			{
				File.Delete(copy);
			}
		}

		[TestMethod]
		public void ReturnsNullForMissingFile()
		{
			Assert.IsNull(FontThumbnailRenderer.RenderToPng("/nonexistent/none.ttf", 64));
		}

		[TestMethod]
		public void RendersInstalledFontWhenAvailable()
		{
			var font = new[] { "/usr/share/fonts", "/usr/share/fonts/truetype" }
				.Where(Directory.Exists)
				.SelectMany(d => Directory.EnumerateFiles(d, "*.ttf", SearchOption.AllDirectories).Take(20))
				.FirstOrDefault(FontThumbnailRenderer.IsSystemInstalledFontPath);
			if (font is null)
				Assert.Inconclusive("No trusted system font installed");

			var png = FontThumbnailRenderer.RenderToPng(font, 64);
			Assert.IsNotNull(png);
			using var bitmap = SKBitmap.Decode(png);
			Assert.AreEqual(64, bitmap.Width);
			Assert.IsTrue(Enumerable.Range(0, 64).Any(y => Enumerable.Range(0, 64).Any(x => bitmap.GetPixel(x, y).Alpha > 0)));
		}
	}
}
