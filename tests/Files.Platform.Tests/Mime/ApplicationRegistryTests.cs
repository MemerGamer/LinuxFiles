// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Mime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Mime
{
	[TestClass]
	public sealed class ApplicationRegistryTests
	{
		private static LinuxApplicationRegistry Create(XdgFixture fx, string culture = "en-US", params string[] executables) =>
			new(fx.Directories, CultureInfo.GetCultureInfo(culture), new FakeLocator(executables));

		[TestMethod]
		public async Task Cache_ReusesDesktopParsingAndInvalidatesReplacementAndDeletion()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "edit.desktop", "Before", "edit %f");
			var path = Path.Combine(fx.SystemData, "applications", "edit.desktop");
			var registry = Create(fx);
			var before = await registry.GetApplicationAsync("edit.desktop");
			Assert.AreSame(before, await registry.GetApplicationAsync("edit.desktop"));
			var timestamp = File.GetLastWriteTimeUtc(path);
			var replacement = path + ".new";
			File.WriteAllText(replacement, File.ReadAllText(path).Replace("Before", "After!"));
			File.SetLastWriteTimeUtc(replacement, timestamp);
			File.Move(replacement, path, true);
			Assert.AreEqual("After!", (await registry.GetApplicationAsync("edit.desktop"))!.Name);
			File.Delete(path);
			Assert.IsNull(await registry.GetApplicationAsync("edit.desktop"));
		}

		[TestMethod]
		public async Task Cache_InvalidatesMimeInfoAndAssociationsAndUserOverride()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "a.desktop", "A", "a %f");
			fx.WriteDesktop("usr-share", "b.desktop", "B", "b %f");
			fx.Write("usr-share/applications/mimeinfo.cache", "[MIME Cache]\ntext/plain=a.desktop;\n");
			var registry = Create(fx);
			Assert.AreEqual("a.desktop", (await registry.GetApplicationsForMimeTypeAsync("text/plain")).Single().Id);
			fx.Write("usr-share/applications/mimeinfo.cache", "[MIME Cache]\ntext/plain=b.desktop;a.desktop;\n");
			CollectionAssert.AreEqual(new[] { "b.desktop", "a.desktop" }, (await registry.GetApplicationsForMimeTypeAsync("text/plain")).Select(a => a.Id).ToArray());
			fx.Write("config/mimeapps.list", "[Removed Associations]\ntext/plain=b.desktop;\n");
			Assert.AreEqual("a.desktop", (await registry.GetApplicationsForMimeTypeAsync("text/plain")).Single().Id);
			await registry.SetDefaultApplicationAsync("text/plain", "b.desktop");
			Assert.AreEqual("b.desktop", (await registry.GetDefaultApplicationAsync("text/plain"))!.Id);
			fx.WriteDesktop("data", "b.desktop", "User B", "b %f");
			Assert.AreEqual("User B", (await registry.GetDefaultApplicationAsync("text/plain"))!.Name);
		}

		[TestMethod]
		public async Task Cache_FallbackScanSeesNewAndChangedNestedEntries()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "vendor/a.desktop", "A", "a %f", "MimeType=text/plain;\n");
			var registry = Create(fx);
			Assert.AreEqual(1, (await registry.GetApplicationsForMimeTypeAsync("text/plain")).Count);
			fx.WriteDesktop("usr-share", "vendor/b.desktop", "B", "b %f", "MimeType=text/plain;\n");
			Assert.AreEqual(2, (await registry.GetApplicationsForMimeTypeAsync("text/plain")).Count);
			fx.WriteDesktop("usr-share", "vendor/a.desktop", "A", "a %f", "MimeType=image/png;\n");
			Assert.AreEqual("vendor-b.desktop", (await registry.GetApplicationsForMimeTypeAsync("text/plain")).Single().Id);
		}

		[TestMethod]
		public async Task Cache_ConcurrentQueriesShareParsedEntry()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "a.desktop", "A", "a %f");
			var registry = Create(fx);
			var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => registry.GetApplicationAsync("a.desktop")));
			foreach (var result in results) Assert.AreSame(results[0], result);
		}

		[TestMethod]
		public async Task Parse_LocalizedNameAndFlags()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "edit.desktop", "Editor", "edit %f",
				"Name[hu]=Szerkeszto\nName[de_AT]=Bearbeiter\nGenericName=Text\nComment=c\nIcon=edit-icon\nTerminal=true\nMimeType=text/plain;text/x-c;\n");
			fx.Write("usr-share/applications/mimeinfo.cache", "[MIME Cache]\ntext/plain=edit.desktop;\n");

			var hu = await Create(fx, "hu-HU").GetApplicationAsync("edit.desktop");
			var de = await Create(fx, "de-AT").GetApplicationAsync("edit.desktop");
			var fr = await Create(fx, "fr-FR").GetApplicationAsync("edit.desktop");

			Assert.AreEqual("Szerkeszto", hu!.Name);
			Assert.AreEqual("Bearbeiter", de!.Name);
			Assert.AreEqual("Editor", fr!.Name);
			Assert.AreEqual("Text", fr.GenericName);
			Assert.AreEqual("edit-icon", fr.IconName);
			Assert.IsTrue(fr.RunInTerminal);
			CollectionAssert.AreEqual(new[] { "text/plain", "text/x-c" }, fr.MimeTypes!.ToArray());
			Assert.AreEqual(Path.Combine(fx.SystemData, "applications", "edit.desktop"), fr.DesktopFilePath);
		}

		[TestMethod]
		public async Task GetAllApplications_ListsVisibleAppsSortedAndUserOverridesSystem()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "zed.desktop", "Zed", "zed %f");
			fx.WriteDesktop("usr-share", "alpha.desktop", "Alpha", "alpha %f");
			fx.WriteDesktop("usr-share", "nodisplay.desktop", "Hidden Helper", "h", "NoDisplay=true\n");
			fx.WriteDesktop("data", "alpha.desktop", "Alpha User", "alpha-user %f");

			var apps = await Create(fx).GetAllApplicationsAsync();

			CollectionAssert.AreEqual(new[] { "Alpha User", "Zed" }, apps.Select(a => a.Name).ToArray());
		}

		[TestMethod]
		public async Task Hidden_TryExecMissing_AndNonApplication_AreRejected()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "hidden.desktop", "H", "h", "Hidden=true\n");
			fx.WriteDesktop("usr-share", "tryexec.desktop", "T", "t", "TryExec=missing-tool\n");
			fx.WriteDesktop("usr-share", "tryexec-ok.desktop", "T2", "t", "TryExec=present-tool\n");
			fx.Write("usr-share/applications/link.desktop", "[Desktop Entry]\nType=Link\nName=L\nURL=http://x\n");

			var reg = Create(fx, "en-US", "present-tool");
			Assert.IsNull(await reg.GetApplicationAsync("hidden.desktop"));
			Assert.IsNull(await reg.GetApplicationAsync("tryexec.desktop"));
			Assert.IsNotNull(await reg.GetApplicationAsync("tryexec-ok.desktop"));
			Assert.IsNull(await reg.GetApplicationAsync("link.desktop"));
			Assert.IsNull(await reg.GetApplicationAsync("../escape.desktop"));
		}

		[TestMethod]
		public async Task Desktop_IdWithDashMapsToSubdirectory()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "vendor/app.desktop", "Vendor App", "app");

			Assert.AreEqual("Vendor App", (await Create(fx).GetApplicationAsync("vendor-app.desktop"))!.Name);
		}

		[TestMethod]
		public async Task List_UsesMimeInfoCache_ExcludesNoDisplay_AndFirstIsFallbackDefault()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "a.desktop", "A", "a %f");
			fx.WriteDesktop("usr-share", "b.desktop", "B", "b %f");
			fx.WriteDesktop("usr-share", "c.desktop", "C", "c %f", "NoDisplay=true\n");
			fx.Write("usr-share/applications/mimeinfo.cache", "[MIME Cache]\ntext/plain=c.desktop;a.desktop;b.desktop;\nimage/png=b.desktop;\n");

			var reg = Create(fx);
			var apps = await reg.GetApplicationsForMimeTypeAsync("text/plain");

			CollectionAssert.AreEqual(new[] { "a.desktop", "b.desktop" }, apps.Select(a => a.Id).ToArray());
			Assert.AreEqual("a.desktop", (await reg.GetDefaultApplicationAsync("text/plain"))!.Id);
			Assert.IsNull(await reg.GetDefaultApplicationAsync("video/mp4"));
		}

		[TestMethod]
		public async Task List_ScansDesktopFiles_WhenCacheIsMissing()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "a.desktop", "A", "a %f", "MimeType=text/plain;\n");
			fx.WriteDesktop("usr-share", "b.desktop", "B", "b %f", "MimeType=image/png;\n");

			var apps = await Create(fx).GetApplicationsForMimeTypeAsync("text/plain");

			CollectionAssert.AreEqual(new[] { "a.desktop" }, apps.Select(a => a.Id).ToArray());
		}

		[TestMethod]
		public async Task Default_PrecedenceUserOverSystem_AndDesktopSpecificFirst()
		{
			using var fx = new XdgFixture { CurrentDesktop = "KDE" };
			foreach (var id in new[] { "sys", "usr", "kde", "etc" })
				fx.WriteDesktop("usr-share", id + ".desktop", id, id + " %f");

			fx.Write("usr-share/applications/mimeapps.list", "[Default Applications]\ntext/plain=sys.desktop;\n");
			fx.Write("etc-xdg/mimeapps.list", "[Default Applications]\ntext/plain=etc.desktop;\n");
			fx.Write("config/mimeapps.list", "[Default Applications]\ntext/plain=usr.desktop;\n");

			var reg = Create(fx);
			Assert.AreEqual("usr.desktop", (await reg.GetDefaultApplicationAsync("text/plain"))!.Id);

			fx.Write("config/kde-mimeapps.list", "[Default Applications]\ntext/plain=kde.desktop;\n");
			Assert.AreEqual("kde.desktop", (await reg.GetDefaultApplicationAsync("text/plain"))!.Id);
		}

		[TestMethod]
		public async Task Default_SkipsUninstalledEntries()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "real.desktop", "Real", "real %f");
			fx.Write("config/mimeapps.list", "[Default Applications]\ntext/plain=ghost.desktop;real.desktop;\n");

			Assert.AreEqual("real.desktop", (await Create(fx).GetDefaultApplicationAsync("text/plain"))!.Id);
		}

		[TestMethod]
		public async Task AddedAndRemovedAssociations_AreApplied()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "a.desktop", "A", "a %f");
			fx.WriteDesktop("usr-share", "b.desktop", "B", "b %f");
			fx.WriteDesktop("usr-share", "x.desktop", "X", "x %f");
			fx.Write("usr-share/applications/mimeinfo.cache", "[MIME Cache]\ntext/plain=a.desktop;b.desktop;\n");
			fx.Write("config/mimeapps.list", "[Added Associations]\ntext/plain=x.desktop;\n[Removed Associations]\ntext/plain=a.desktop;\n");

			var apps = await Create(fx).GetApplicationsForMimeTypeAsync("text/plain");

			CollectionAssert.AreEqual(new[] { "x.desktop", "b.desktop" }, apps.Select(a => a.Id).ToArray());
		}

		[TestMethod]
		public async Task SetDefault_WritesUserMimeApps_AndPreservesOtherLines()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "a.desktop", "A", "a %f");
			fx.WriteDesktop("usr-share", "b.desktop", "B", "b %f");
			fx.Write("config/mimeapps.list",
				"# keep\n[Added Associations]\nimage/png=a.desktop;\n\n[Default Applications]\nimage/png=a.desktop;\ntext/plain=a.desktop;\n\n[Removed Associations]\ntext/plain=b.desktop;a.desktop;\n");

			var reg = Create(fx);
			await reg.SetDefaultApplicationAsync("text/plain", "b.desktop");

			var text = File.ReadAllText(Path.Combine(fx.ConfigHome, "mimeapps.list"));
			StringAssert.Contains(text, "# keep\n");
			StringAssert.Contains(text, "image/png=a.desktop;\n");
			StringAssert.Contains(text, "text/plain=b.desktop;a.desktop;\n");
			StringAssert.Contains(text, "[Removed Associations]\ntext/plain=a.desktop;\n");
			Assert.AreEqual("b.desktop", (await reg.GetDefaultApplicationAsync("text/plain"))!.Id);
		}

		[TestMethod]
		public async Task SetDefault_CreatesFileAndSection()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "a.desktop", "A", "a %f");

			var reg = Create(fx);
			await reg.SetDefaultApplicationAsync("image/png", "a.desktop");

			Assert.AreEqual("[Default Applications]\nimage/png=a.desktop;\n", File.ReadAllText(Path.Combine(fx.ConfigHome, "mimeapps.list")));
			Assert.AreEqual("a.desktop", (await reg.GetDefaultApplicationAsync("image/png"))!.Id);
		}
	}
}
