// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Icons;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Icons
{
	[TestClass]
	public sealed class IconThemeTests
	{
		private string _root = string.Empty;
		private string _icons = string.Empty;
		private string _pixmaps = string.Empty;
		private string _config = string.Empty;

		[TestInitialize]
		public void Setup()
		{
			_root = Path.Combine(Path.GetTempPath(), "files-icons-" + Guid.NewGuid().ToString("N"));
			_icons = Path.Combine(_root, "icons");
			_pixmaps = Path.Combine(_root, "pixmaps");
			_config = Path.Combine(_root, "config");
			Directory.CreateDirectory(_icons);
			Directory.CreateDirectory(_pixmaps);
			Directory.CreateDirectory(_config);

			// Child inherits Parent; Parent inherits hicolor implicitly via the spec fallback.
			WriteTheme("Child", "Inherits=Parent", ("16x16/places", 16, "Fixed"), ("48x48/places", 48, "Fixed"), ("scalable/apps", 0, "Scalable"));
			WriteTheme("Parent", "Inherits=hicolor", ("32x32/places", 32, "Fixed"), ("32x32/mimetypes", 32, "Fixed"));
			WriteTheme("hicolor", "", ("64x64/places", 64, "Fixed"));
			Touch("Child", "16x16/places", "folder.png");
			Touch("Child", "48x48/places", "folder.png");
			Touch("Child", "scalable/apps", "app.svg");
			Touch("Parent", "32x32/places", "user-home.png");
			Touch("Parent", "32x32/places", "folder.png");
			Touch("Parent", "32x32/mimetypes", "text-x-generic.png");
			Touch("hicolor", "64x64/places", "drive-harddisk.png");
			File.WriteAllText(Path.Combine(_pixmaps, "legacy.xpm"), "x");
		}

		[TestCleanup]
		public void Cleanup()
		{
			Directory.Delete(_root, true);
		}

		private LinuxIconThemeProvider CreateProvider(string? theme = "Child", string? desktop = null) => new(new LinuxIconThemeOptions
		{
			IconDirectories = [_icons],
			PixmapDirectories = [_pixmaps],
			ConfigHome = _config,
			CurrentDesktop = desktop,
			ThemeName = theme,
		});

		private void WriteTheme(string name, string inherits, params (string Path, int Size, string Type)[] dirs)
		{
			var dirNames = string.Join(",", Array.ConvertAll(dirs, d => d.Path));
			var text = "[Icon Theme]\nName=" + name + "\n" + inherits + "\nDirectories=" + dirNames + "\n";
			foreach (var (path, size, type) in dirs)
			{
				text += "\n[" + path + "]\nSize=" + (type == "Scalable" ? 48 : size) + "\nType=" + type + "\n";
				if (type == "Scalable")
					text += "MinSize=8\nMaxSize=512\n";
			}

			Directory.CreateDirectory(Path.Combine(_icons, name));
			File.WriteAllText(Path.Combine(_icons, name, "index.theme"), text);
		}

		private void Touch(string theme, string dir, string file)
		{
			var d = Path.Combine(_icons, theme, dir);
			Directory.CreateDirectory(d);
			File.WriteAllText(Path.Combine(d, file), "x");
		}

		[TestMethod]
		public void Index_ParsesDirectoriesAndInherits()
		{
			var index = IconThemeIndex.Parse("[Icon Theme]\nInherits=A, B\nDirectories=x,y\n\n[x]\nSize=16\nType=Threshold\nThreshold=4\n\n[y]\nSize=32\nScale=2\nType=Fixed\n");

			CollectionAssert.AreEqual(new[] { "A", "B" }, new System.Collections.Generic.List<string>(index.Inherits));
			Assert.AreEqual(2, index.Directories.Count);
			Assert.AreEqual(IconDirectoryType.Threshold, index.Directories[0].Type);
			Assert.AreEqual(4, index.Directories[0].Threshold);
			Assert.AreEqual(2, index.Directories[1].Scale);
		}

		[TestMethod]
		public async Task Resolve_PicksExactSizeDirectory()
		{
			var provider = CreateProvider();

			var result = await provider.ResolveIconAsync("folder", 48);

			Assert.AreEqual(Path.Combine(_icons, "Child", "48x48/places", "folder.png"), result!.Value.Path);
			Assert.IsFalse(result.Value.IsSvg);
		}

		[TestMethod]
		public async Task Resolve_PicksClosestSizeWhenNoExactMatch()
		{
			var provider = CreateProvider();

			Assert.AreEqual(Path.Combine(_icons, "Child", "16x16/places", "folder.png"), (await provider.ResolveIconAsync("folder", 20))!.Value.Path);
			Assert.AreEqual(Path.Combine(_icons, "Child", "48x48/places", "folder.png"), (await provider.ResolveIconAsync("folder", 40))!.Value.Path);
		}

		[TestMethod]
		public async Task Resolve_PrefersScalableArtworkAtLargeAndHiDpiSizes()
		{
			WriteTheme("Vector", "", ("48x48/places", 48, "Fixed"), ("scalable/places", 0, "Scalable"));
			Touch("Vector", "48x48/places", "folder.png");
			Touch("Vector", "scalable/places", "folder.svg");
			var provider = CreateProvider("Vector");

			foreach (var size in new uint[] { 196, 392, 600 })
			{
				var result = await provider.ResolveIconAsync("folder", size);
				Assert.AreEqual(Path.Combine(_icons, "Vector", "scalable/places", "folder.svg"), result!.Value.Path);
				Assert.IsTrue(result.Value.IsSvg);
			}
		}

		[TestMethod]
		public async Task Resolve_PrefersSvgOverRasterInSameDirectory()
		{
			Touch("Child", "48x48/places", "folder.svg");
			var result = await CreateProvider().ResolveIconAsync("folder", 48);

			Assert.AreEqual(Path.Combine(_icons, "Child", "48x48/places", "folder.svg"), result!.Value.Path);
			Assert.IsTrue(result.Value.IsSvg);
		}

		[TestMethod]
		public async Task Resolve_FollowsInheritsChain()
		{
			var provider = CreateProvider();

			var home = await provider.ResolveIconAsync("user-home", 48);
			var drive = await provider.ResolveIconAsync("drive-harddisk", 48);

			Assert.AreEqual(Path.Combine(_icons, "Parent", "32x32/places", "user-home.png"), home!.Value.Path);
			Assert.AreEqual(Path.Combine(_icons, "hicolor", "64x64/places", "drive-harddisk.png"), drive!.Value.Path);
		}

		[TestMethod]
		public async Task Resolve_FallsBackToHicolorWhenNotInherited()
		{
			WriteTheme("Lonely", "", ("16x16/places", 16, "Fixed"));
			var provider = CreateProvider("Lonely");

			var drive = await provider.ResolveIconAsync("drive-harddisk", 32);

			Assert.AreEqual(Path.Combine(_icons, "hicolor", "64x64/places", "drive-harddisk.png"), drive!.Value.Path);
		}

		[TestMethod]
		public async Task Resolve_ScalableSvgIsFlagged()
		{
			var provider = CreateProvider();

			var result = await provider.ResolveIconAsync("app", 128);

			Assert.IsTrue(result!.Value.IsSvg);
		}

		[TestMethod]
		public async Task Resolve_StripsDashSuffixesAsFallback()
		{
			var provider = CreateProvider();

			var result = await provider.ResolveIconAsync("folder-documents", 48);

			Assert.AreEqual(Path.Combine(_icons, "Child", "48x48/places", "folder.png"), result!.Value.Path);
		}

		[TestMethod]
		public async Task Resolve_UsesPixmapsAndAbsolutePathsAndReportsMisses()
		{
			var provider = CreateProvider();

			Assert.AreEqual(Path.Combine(_pixmaps, "legacy.xpm"), (await provider.ResolveIconAsync("legacy", 32))!.Value.Path);
			Assert.AreEqual(Path.Combine(_pixmaps, "legacy.xpm"), (await provider.ResolveIconAsync(Path.Combine(_pixmaps, "legacy.xpm"), 32))!.Value.Path);
			Assert.IsNull(await provider.ResolveIconAsync("does-not-exist-anywhere", 32));
			Assert.IsNull(await provider.ResolveIconAsync("", 32));
		}

		[TestMethod]
		public async Task Resolve_ListReturnsFirstHit()
		{
			var provider = CreateProvider();

			var result = await provider.ResolveIconAsync(["nope", "text-x-generic", "folder"], 32);

			Assert.AreEqual(Path.Combine(_icons, "Parent", "32x32/mimetypes", "text-x-generic.png"), result!.Value.Path);
		}

		[TestMethod]
		public async Task Candidates_IncludeAlternateSizesAndInheritedThemesInOrder()
		{
			WriteTheme("breeze", "Inherits=Child", ("scalable/places", 48, "Scalable"));
			Touch("hicolor", "64x64/places", "folder.svg");
			Touch("breeze", "scalable/places", "folder.svg");
			File.CreateSymbolicLink(Path.Combine(_icons, "Child", "16x16/places", "inode-directory.png"), "folder.png");

			var results = await CreateProvider().ResolveIconCandidatesAsync(["folder", "inode-directory"], 16);

			CollectionAssert.AreEqual(new[]
			{
				Path.Combine(_icons, "Child", "16x16/places", "folder.png"),
				Path.Combine(_icons, "Child", "48x48/places", "folder.png"),
				Path.Combine(_icons, "Parent", "32x32/places", "folder.png"),
				Path.Combine(_icons, "hicolor", "64x64/places", "folder.svg"),
				Path.Combine(_icons, "breeze", "scalable/places", "folder.svg"),
				Path.Combine(_icons, "Child", "16x16/places", "inode-directory.png"),
			}, results.Select(r => r.Path).ToArray());
		}

		[TestMethod]
		public async Task Candidates_SkipBrokenLinksAndIncludeScalableFolderVariants()
		{
			File.Delete(Path.Combine(_icons, "Child", "16x16/places", "folder.png"));
			File.CreateSymbolicLink(Path.Combine(_icons, "Child", "16x16/places", "folder.png"), "missing.png");
			Touch("Child", "scalable/apps", "folder-documents.svg");
			var provider = CreateProvider();

			var results = await provider.ResolveIconCandidatesAsync(["folder-documents", "folder"], 16);

			Assert.AreEqual(Path.Combine(_icons, "Child", "scalable/apps", "folder-documents.svg"), results[0].Path);
			Assert.AreEqual(Path.Combine(_icons, "Child", "48x48/places", "folder.png"), results[1].Path);
			Assert.AreEqual(3, results.Count);
		}

		[TestMethod]
		public void Theme_DetectedFromKdeGlobalsOnKde()
		{
			File.WriteAllText(Path.Combine(_config, "kdeglobals"), "[General]\nx=1\n\n[Icons]\nTheme=Parent\n");
			Directory.CreateDirectory(Path.Combine(_config, "gtk-3.0"));
			File.WriteAllText(Path.Combine(_config, "gtk-3.0", "settings.ini"), "[Settings]\ngtk-icon-theme-name=Child\n");

			Assert.AreEqual("Parent", CreateProvider(null, "KDE").CurrentThemeName);
			Assert.AreEqual("Child", CreateProvider(null, "GNOME").CurrentThemeName);
		}

		[TestMethod]
		public void Theme_FallsBackWhenConfiguredThemeIsMissing()
		{
			Directory.CreateDirectory(Path.Combine(_config, "gtk-3.0"));
			File.WriteAllText(Path.Combine(_config, "gtk-3.0", "settings.ini"), "[Settings]\ngtk-icon-theme-name=Uninstalled\n");

			Assert.AreEqual("hicolor", CreateProvider(null, "GNOME").CurrentThemeName);

			WriteTheme("Adwaita", "", ("16x16/places", 16, "Fixed"));
			Assert.AreEqual("Adwaita", CreateProvider(null, "GNOME").CurrentThemeName);
		}
	}
}
