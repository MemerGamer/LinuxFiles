// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Mime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Mime
{
	[TestClass]
	public sealed class MimeTypeServiceTests
	{
		private const string Globs2 =
			"# comment\n" +
			"50:text/plain:*.txt\n" +
			"50:application/gzip:*.gz\n" +
			"50:application/x-compressed-tar:*.tar.gz\n" +
			"50:image/jpeg:*.jpg:cs\n" +
			"50:image/x-jpg-lower:*.jpg\n" +
			"60:text/x-makefile:Makefile\n" +
			"50:image/png:*.png\n" +
			"80:application/x-special:*.png\n" +
			"50:text/x-readme:README*\n";

		private static LinuxMimeTypeService Create(XdgFixture fx, string lang = "en-US")
		{
			fx.Write("usr-share/mime/globs2", Globs2);
			return new LinuxMimeTypeService(fx.Directories, CultureInfo.GetCultureInfo(lang));
		}

		[TestMethod]
		public async Task Glob_SimpleExtension_CaseInsensitive()
		{
			using var fx = new XdgFixture();
			var svc = Create(fx);

			Assert.AreEqual("text/plain", await svc.GetMimeTypeAsync("/nowhere/NOTES.TXT"));
		}

		[TestMethod]
		public async Task Glob_LongestPatternWins()
		{
			using var fx = new XdgFixture();
			var svc = Create(fx);

			Assert.AreEqual("application/x-compressed-tar", await svc.GetMimeTypeAsync("/nowhere/a.tar.gz"));
			Assert.AreEqual("application/gzip", await svc.GetMimeTypeAsync("/nowhere/a.gz"));
		}

		[TestMethod]
		public async Task Glob_HigherWeightBeatsLength()
		{
			using var fx = new XdgFixture();
			var svc = Create(fx);

			Assert.AreEqual("application/x-special", await svc.GetMimeTypeAsync("/nowhere/a.png"));
		}

		[TestMethod]
		public async Task Glob_CaseSensitiveFlag()
		{
			using var fx = new XdgFixture();
			var svc = Create(fx);

			Assert.AreEqual("image/jpeg", await svc.GetMimeTypeAsync("/nowhere/a.jpg"));
			Assert.AreEqual("image/x-jpg-lower", await svc.GetMimeTypeAsync("/nowhere/a.JPG"));
		}

		[TestMethod]
		public async Task Glob_LiteralAndPrefixPatterns()
		{
			using var fx = new XdgFixture();
			var svc = Create(fx);

			Assert.AreEqual("text/x-makefile", await svc.GetMimeTypeAsync("/nowhere/Makefile"));
			Assert.AreEqual("text/x-readme", await svc.GetMimeTypeAsync("/nowhere/README.md"));
		}

		[TestMethod]
		public async Task Glob_DatabasesAreMergedAcrossDataDirs()
		{
			using var fx = new XdgFixture();
			var svc = Create(fx);
			fx.Write("data/mime/globs2", "50:application/x-custom:*.cust\n");

			Assert.AreEqual("application/x-custom", await new LinuxMimeTypeService(fx.Directories, CultureInfo.InvariantCulture).GetMimeTypeAsync("/x/a.cust"));
			Assert.AreEqual("text/plain", await svc.GetMimeTypeAsync("/x/a.txt"));
		}

		[TestMethod]
		public async Task Directory_IsInodeDirectory_AndSymlinkToDirectoryToo()
		{
			using var fx = new XdgFixture();
			var svc = Create(fx);
			var dir = Path.Combine(fx.Root, "folder.txt");
			Directory.CreateDirectory(dir);
			var link = Path.Combine(fx.Root, "linkdir");
			Directory.CreateSymbolicLink(link, dir);

			Assert.AreEqual("inode/directory", await svc.GetMimeTypeAsync(dir));
			Assert.AreEqual("inode/directory", await svc.GetMimeTypeAsync(link));
		}

		[TestMethod]
		public async Task Symlink_Dangling_IsInodeSymlink_AndValidUsesLinkName()
		{
			using var fx = new XdgFixture();
			var svc = Create(fx);
			var target = fx.Write("t/real.bin", "hello");
			var good = Path.Combine(fx.Root, "alias.txt");
			File.CreateSymbolicLink(good, target);
			var dangling = Path.Combine(fx.Root, "broken");
			File.CreateSymbolicLink(dangling, Path.Combine(fx.Root, "missing"));

			Assert.AreEqual("text/plain", await svc.GetMimeTypeAsync(good));
			Assert.AreEqual("inode/symlink", await svc.GetMimeTypeAsync(dangling));
		}

		[TestMethod]
		public async Task Sniff_MagicNumbersAndText()
		{
			using var fx = new XdgFixture();
			var svc = Create(fx);

			Assert.AreEqual("image/png", await svc.GetMimeTypeAsync(fx.WriteBytes("s/a", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0])));
			Assert.AreEqual("application/pdf", await svc.GetMimeTypeAsync(fx.WriteBytes("s/b", "%PDF-1.7"u8.ToArray())));
			Assert.AreEqual("application/x-executable", await svc.GetMimeTypeAsync(fx.WriteBytes("s/c", [0x7F, 0x45, 0x4C, 0x46, 2, 1])));
			Assert.AreEqual("text/x-shellscript", await svc.GetMimeTypeAsync(fx.Write("s/d", "#!/bin/sh\necho\n")));
			Assert.AreEqual("text/plain", await svc.GetMimeTypeAsync(fx.Write("s/e", "plain text, ünïcode\n")));
			Assert.AreEqual("application/octet-stream", await svc.GetMimeTypeAsync(fx.WriteBytes("s/f", [1, 2, 0, 3, 255])));
			Assert.AreEqual("application/x-zerosize", await svc.GetMimeTypeAsync(fx.Write("s/g", string.Empty)));
		}

		[TestMethod]
		public async Task Unknown_MissingPath_IsOctetStream()
		{
			using var fx = new XdgFixture();
			var svc = Create(fx);

			Assert.AreEqual("application/octet-stream", await svc.GetMimeTypeAsync("/nowhere/file.unknownext"));
		}

		[TestMethod]
		public void GlobMatch_Patterns()
		{
			Assert.IsTrue(LinuxMimeTypeService.GlobMatch("*.txt", "a.txt"));
			Assert.IsFalse(LinuxMimeTypeService.GlobMatch("*.txt", "a.txt.bak"));
			Assert.IsTrue(LinuxMimeTypeService.GlobMatch("a?c", "abc"));
			Assert.IsTrue(LinuxMimeTypeService.GlobMatch("[a-c]x", "bx"));
			Assert.IsFalse(LinuxMimeTypeService.GlobMatch("[!a-c]x", "bx"));
			Assert.IsTrue(LinuxMimeTypeService.GlobMatch("*a*b*", "xxaYYbzz"));
			Assert.IsTrue(LinuxMimeTypeService.GlobMatch("*", ""));
		}

		private const string PngXml =
			"<?xml version=\"1.0\"?>\n" +
			"<mime-type xmlns=\"http://www.freedesktop.org/standards/shared-mime-info\" type=\"image/png\">\n" +
			"  <comment>PNG image</comment>\n" +
			"  <comment xml:lang=\"hu\">PNG kép</comment>\n" +
			"  <comment xml:lang=\"de_AT\">PNG-Bild (AT)</comment>\n" +
			"  <icon name=\"image-png-special\"/>\n" +
			"  <generic-icon name=\"image-x-generic\"/>\n" +
			"</mime-type>\n";

		[TestMethod]
		public async Task Description_IsLocalizedWithFallback()
		{
			using var fx = new XdgFixture();
			fx.Write("usr-share/mime/image/png.xml", PngXml);

			Assert.AreEqual("PNG kép", await new LinuxMimeTypeService(fx.Directories, CultureInfo.GetCultureInfo("hu-HU")).GetDescriptionAsync("image/png"));
			Assert.AreEqual("PNG-Bild (AT)", await new LinuxMimeTypeService(fx.Directories, CultureInfo.GetCultureInfo("de-AT")).GetDescriptionAsync("image/png"));
			Assert.AreEqual("PNG image", await new LinuxMimeTypeService(fx.Directories, CultureInfo.GetCultureInfo("fr-FR")).GetDescriptionAsync("image/png"));
			Assert.IsNull(await new LinuxMimeTypeService(fx.Directories, CultureInfo.InvariantCulture).GetDescriptionAsync("image/unknown"));
		}

		[TestMethod]
		public async Task Icons_ExplicitAndDerived()
		{
			using var fx = new XdgFixture();
			fx.Write("usr-share/mime/image/png.xml", PngXml);
			var svc = new LinuxMimeTypeService(fx.Directories, CultureInfo.InvariantCulture);

			Assert.AreEqual("image-png-special", await svc.GetIconNameAsync("image/png"));
			Assert.AreEqual("image-x-generic", await svc.GetGenericIconNameAsync("image/png"));
			Assert.AreEqual("text-x-python", await svc.GetIconNameAsync("text/x-python"));
			Assert.AreEqual("text-x-generic", await svc.GetGenericIconNameAsync("text/x-python"));
			Assert.AreEqual("folder", await svc.GetGenericIconNameAsync("inode/directory"));
		}
	}
}
