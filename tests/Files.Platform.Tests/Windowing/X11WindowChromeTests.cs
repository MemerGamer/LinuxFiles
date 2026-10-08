// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Windowing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Platform.Tests.Windowing
{
	[TestClass]
	public sealed class X11WindowChromeTests
	{
		[TestMethod]
		public void SanitizeTitle_KeepsPrintableText()
		{
			const string title = "Documents ü 日本 - LinuxFiles";

			Assert.AreSame(title, X11WindowChrome.SanitizeTitle(title));
		}

		[TestMethod]
		public void SanitizeTitle_ReplacesControlCharacters()
		{
			Assert.AreEqual("a b c d e f - LinuxFiles", X11WindowChrome.SanitizeTitle("a\nb\0c\u001bd\u0085e\u007ff - LinuxFiles"));
		}

		[TestMethod]
		public void ToOpacityCardinal_ClampsAndScales()
		{
			Assert.AreEqual(uint.MaxValue, X11WindowChrome.ToOpacityCardinal(1d));
			Assert.AreEqual(uint.MaxValue, X11WindowChrome.ToOpacityCardinal(7d));
			Assert.AreEqual(uint.MaxValue, X11WindowChrome.ToOpacityCardinal(double.NaN));
			Assert.AreEqual(0u, X11WindowChrome.ToOpacityCardinal(-1d));
			Assert.AreEqual(2147483648u, X11WindowChrome.ToOpacityCardinal(0.5d));
		}
	}
}
