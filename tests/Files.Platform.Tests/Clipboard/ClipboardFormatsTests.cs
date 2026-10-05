// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Clipboard;
using Files.Platform.Linux.Clipboard;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Text;

namespace Files.Platform.Tests.Clipboard
{
	[TestClass]
	public sealed class ClipboardFormatsTests
	{
		[TestMethod]
		public void PathToUri_EncodesSpecialAndNonAsciiCharacters()
		{
			Assert.AreEqual("file:///home/u/a%20b%23c%3Fd%25e.txt", ClipboardFormats.PathToUri("/home/u/a b#c?d%e.txt"));
			Assert.AreEqual("file:///home/u/%C3%A1rv%C3%ADzt%C5%B1r%C5%91", ClipboardFormats.PathToUri("/home/u/árvíztűrő"));
			Assert.AreEqual("file:///tmp/A-b_c.d~e/f", ClipboardFormats.PathToUri("/tmp/A-b_c.d~e/f"));
			Assert.AreEqual("file:///tmp/%5Bx%5D%20%26%20%27q%27", ClipboardFormats.PathToUri("/tmp/[x] & 'q'"));
		}

		[TestMethod]
		public void UriToPath_RoundTripsAwkwardNames()
		{
			foreach (var path in new[] { "/a b/c#d", "/x/y?z", "/100%/done", "/árvíztűrő/😀.txt", "/semi;colon/amp&", "/new\nline" })
				Assert.AreEqual(path, ClipboardFormats.UriToPath(ClipboardFormats.PathToUri(path)), path);
		}

		[TestMethod]
		public void UriToPath_RejectsUntrustedForms()
		{
			Assert.IsNull(ClipboardFormats.UriToPath("http://example.com/a"));
			Assert.IsNull(ClipboardFormats.UriToPath("file://otherhost/etc/passwd"));
			Assert.IsNull(ClipboardFormats.UriToPath("file:relative/path"));
			Assert.IsNull(ClipboardFormats.UriToPath("file:///a%00b"));
			Assert.IsNull(ClipboardFormats.UriToPath("file:///a%zz"));
			Assert.IsNull(ClipboardFormats.UriToPath("file:///a%2"));
			Assert.IsNull(ClipboardFormats.UriToPath("file://"));
			Assert.AreEqual("/etc/hosts", ClipboardFormats.UriToPath("file://localhost/etc/hosts"));
			Assert.AreEqual("/a/b", ClipboardFormats.UriToPath("  file:///a/b  "));
		}

		[TestMethod]
		public void UriToPath_KeepsUnencodedNonAscii()
		{
			Assert.AreEqual("/tmp/őz", ClipboardFormats.UriToPath("file:///tmp/őz"));
		}

		[TestMethod]
		public void BuildUriList_UsesCrLfAfterEveryLine()
		{
			Assert.AreEqual("file:///a%20b\r\nfile:///c\r\n", ClipboardFormats.BuildUriList(["/a b", "/c"]));
		}

		[TestMethod]
		public void BuildGnomeCopiedFiles_HasOperationFirstAndNoTrailingNewline()
		{
			Assert.AreEqual("copy\nfile:///a\nfile:///b%20c", ClipboardFormats.BuildGnomeCopiedFiles(["/a", "/b c"], ClipboardOperation.Copy));
			Assert.AreEqual("cut\nfile:///a", ClipboardFormats.BuildGnomeCopiedFiles(["/a"], ClipboardOperation.Cut));
		}

		[TestMethod]
		public void Targets_OfferKdeCutMarkerOnlyForCut()
		{
			Assert.IsFalse(ClipboardFormats.GetTargets(ClipboardOperation.Copy).Contains(ClipboardFormats.KdeCutSelection));
			Assert.IsTrue(ClipboardFormats.GetTargets(ClipboardOperation.Cut).Contains(ClipboardFormats.KdeCutSelection));
			Assert.IsNull(ClipboardFormats.Render(ClipboardFormats.KdeCutSelection, ["/a"], ClipboardOperation.Copy));
			Assert.AreEqual("1", Encoding.UTF8.GetString(ClipboardFormats.Render(ClipboardFormats.KdeCutSelection, ["/a"], ClipboardOperation.Cut)!));
			Assert.IsNull(ClipboardFormats.Render("image/png", ["/a"], ClipboardOperation.Copy));
		}

		[TestMethod]
		public void Render_PlainTextIsOnePathPerLine()
		{
			foreach (var target in new[] { ClipboardFormats.Utf8String, ClipboardFormats.PlainText, ClipboardFormats.PlainTextUtf8 })
				Assert.AreEqual("/a b\n/c", Encoding.UTF8.GetString(ClipboardFormats.Render(target, ["/a b", "/c"], ClipboardOperation.Copy)!));
		}

		[TestMethod]
		public void ParseUriList_SkipsCommentsBlanksAndForeignUris()
		{
			var text = "# comment\r\nfile:///a%20b\r\n\r\nhttp://example.com/x\r\nfile://remote/etc\r\nfile:///c\n";
			CollectionAssert.AreEqual(new[] { "/a b", "/c" }, ClipboardFormats.ParseUriList(text).ToArray());
		}

		[TestMethod]
		public void ParseGnomeCopiedFiles_ReadsOperationAndUris()
		{
			var copy = ClipboardFormats.ParseGnomeCopiedFiles("copy\nfile:///a\nfile:///b");
			Assert.AreEqual(ClipboardOperation.Copy, copy!.Operation);
			CollectionAssert.AreEqual(new[] { "/a", "/b" }, copy.Paths.ToArray());

			var cut = ClipboardFormats.ParseGnomeCopiedFiles("cut\nfile:///a\n");
			Assert.AreEqual(ClipboardOperation.Cut, cut!.Operation);

			Assert.IsNull(ClipboardFormats.ParseGnomeCopiedFiles("link\nfile:///a"));
			Assert.IsNull(ClipboardFormats.ParseGnomeCopiedFiles("copy"));
			Assert.IsNull(ClipboardFormats.ParseGnomeCopiedFiles("cut\nhttp://x/y"));
		}

		[TestMethod]
		public void Combine_PrefersGnomeThenUriListWithKdeMarker()
		{
			var gnome = Encoding.UTF8.GetBytes("cut\nfile:///g");
			var uris = Encoding.UTF8.GetBytes("file:///u\r\n");

			var fromGnome = ClipboardFormats.Combine(gnome, uris, Encoding.UTF8.GetBytes("0"));
			Assert.AreEqual(ClipboardOperation.Cut, fromGnome!.Operation);
			CollectionAssert.AreEqual(new[] { "/g" }, fromGnome.Paths.ToArray());

			var kdeCut = ClipboardFormats.Combine(null, uris, Encoding.UTF8.GetBytes("1"));
			Assert.AreEqual(ClipboardOperation.Cut, kdeCut!.Operation);
			CollectionAssert.AreEqual(new[] { "/u" }, kdeCut.Paths.ToArray());

			Assert.AreEqual(ClipboardOperation.Copy, ClipboardFormats.Combine(null, uris, Encoding.UTF8.GetBytes("0"))!.Operation);
			Assert.AreEqual(ClipboardOperation.Copy, ClipboardFormats.Combine(null, uris, null)!.Operation);
			Assert.IsNull(ClipboardFormats.Combine(null, null, null));
			Assert.IsNull(ClipboardFormats.Combine(null, Encoding.UTF8.GetBytes("# nothing\r\n"), null));
		}

		[TestMethod]
		public void Combine_FallsBackToUriListWhenGnomePayloadIsMalformed()
		{
			var result = ClipboardFormats.Combine(Encoding.UTF8.GetBytes("garbage"), Encoding.UTF8.GetBytes("file:///u"), null);
			CollectionAssert.AreEqual(new[] { "/u" }, result!.Paths.ToArray());
		}
	}
}
