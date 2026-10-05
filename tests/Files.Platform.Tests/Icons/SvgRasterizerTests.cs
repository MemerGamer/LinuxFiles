// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Icons;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace Files.Platform.Tests.Icons
{
	[TestClass]
	public sealed class SvgRasterizerTests
	{
		[TestMethod]
		public void RendersSvgToPng()
		{
			var path = Path.Combine(Path.GetTempPath(), "files-svg-" + Guid.NewGuid().ToString("N") + ".svg");
			File.WriteAllText(path, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" viewBox=\"0 0 16 16\"><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"#ff0000\"/></svg>");
			try
			{
				var png = SvgRasterizer.RenderToPng(path, 32);
				Assert.IsNotNull(png);
				Assert.IsTrue(png.Length > 8);
				Assert.AreEqual(0x89, png[0]);
				Assert.AreEqual((byte)'P', png[1]);
			}
			finally
			{
				File.Delete(path);
			}
		}

		[TestMethod]
		public void ReturnsNullForInvalidSvg()
		{
			var path = Path.Combine(Path.GetTempPath(), "files-svg-" + Guid.NewGuid().ToString("N") + ".svg");
			File.WriteAllText(path, "not svg");
			try
			{
				Assert.IsNull(SvgRasterizer.RenderToPng(path, 32));
			}
			finally
			{
				File.Delete(path);
			}
		}

		[TestMethod]
		public void NormalizeTransforms_InsertsMissingSeparators()
		{
			Assert.AreEqual("<g transform=\"translate(-384.57,-515.8)\" x=\"1-2\"/>", SvgRasterizer.NormalizeTransforms("<g transform=\"translate(-384.57-515.8)\" x=\"1-2\"/>"));
			Assert.AreEqual("<g transform=\"scale(1e-5 2)\"/>", SvgRasterizer.NormalizeTransforms("<g transform=\"scale(1e-5 2)\"/>"));
		}

		[TestMethod]
		public void RendersContentWithSeparatorlessTranslate()
		{
			var path = Path.Combine(Path.GetTempPath(), "files-svg-" + Guid.NewGuid().ToString("N") + ".svg");
			File.WriteAllText(path, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\"><g transform=\"translate(-100-200)\"><rect x=\"102\" y=\"202\" width=\"28\" height=\"28\" fill=\"#ff0000\"/></g></svg>");
			try
			{
				var png = SvgRasterizer.RenderToPng(path, 32);
				Assert.IsNotNull(png);
				using var bitmap = SkiaSharp.SKBitmap.Decode(png);
				Assert.AreEqual(255, bitmap.GetPixel(16, 16).Alpha);
			}
			finally
			{
				File.Delete(path);
			}
		}
	}
}
