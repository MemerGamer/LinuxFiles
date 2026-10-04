// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Launching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace Files.Platform.Tests.Launching
{
	[TestClass]
	public sealed class DisplaySanitizerTests
	{
		[TestMethod]
		[DataRow("\u202E", "\\u202E")]
		[DataRow("\u202A", "\\u202A")]
		[DataRow("\u2066", "\\u2066")]
		[DataRow("\u2069", "\\u2069")]
		[DataRow("\u200E", "\\u200E")]
		[DataRow("\u200F", "\\u200F")]
		[DataRow("\u061C", "\\u061C")]
		[DataRow("\u200B", "\\u200B")]
		[DataRow("\n", "\\u000A")]
		[DataRow("\u001B", "\\u001B")]
		[DataRow("\u2028", "\\u2028")]
		public void Escape_MakesInvisibleCharactersVisible(string input, string expected)
			=> Assert.AreEqual("a" + expected + "b", DisplaySanitizer.Escape("a" + input + "b"));

		[TestMethod]
		public void Escape_KeepsOrdinaryText()
			=> Assert.AreEqual("Szerkeszt\u0151 \u65E5\u672C \U0001F600", DisplaySanitizer.Escape("Szerkeszt\u0151 \u65E5\u672C \U0001F600"));

		[TestMethod]
		public void Field_CapsLengthWithMiddleEllipsis()
		{
			var shown = DisplaySanitizer.Field(new string('a', 150) + new string('b', 150));

			Assert.AreEqual(DisplaySanitizer.MaxFieldLength, shown.Length);
			Assert.IsTrue(shown.StartsWith("aaa"));
			Assert.IsTrue(shown.EndsWith("bbb"));
			Assert.IsTrue(shown.Contains('\u2026'));
			Assert.AreEqual("short", DisplaySanitizer.Field("short"));
		}

		[TestMethod]
		public void Arguments_CapsCountAndEscapesEach()
		{
			var argv = Enumerable.Range(0, 20).Select(i => i == 0 ? "/bin/x\u202E" : "a" + i).ToArray();

			var lines = DisplaySanitizer.Arguments(argv);

			Assert.AreEqual(DisplaySanitizer.MaxArguments + 1, lines.Count);
			Assert.AreEqual("/bin/x\\u202E", lines[0]);
			Assert.AreEqual("\u2026 8 more", lines[^1]);
		}
	}
}
