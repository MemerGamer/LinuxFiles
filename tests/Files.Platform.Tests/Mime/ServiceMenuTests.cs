// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Mime;
using Files.Platform.Linux.Launching;
using Files.Platform.Linux.Mime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Mime
{
	[TestClass]
	public sealed class ServiceMenuTests
	{
		private static string Text(string extra = "", string exec = "tool %F") =>
			"[Desktop Entry]\nType=Service\nMimeType=image/*;\nActions=run;\n" + extra +
			"[Desktop Action run]\nName=Run\nName[fr]=Exécuter\nExec=" + exec + "\n";

		private static ServiceMenuEntry? Parse(string text) => ServiceMenuParser.ParseStrict(text.Split('\n'), "/menu.desktop", CultureInfo.GetCultureInfo("fr-FR"));

		private static LinuxServiceMenuService Service(XdgFixture fx) =>
			new(fx.Directories, new LinuxMimeTypeService(fx.Directories, CultureInfo.InvariantCulture), CultureInfo.GetCultureInfo("fr-FR"));

		[TestMethod]
		public void Parse_LocalizesActionsAndSubmenu_AndPreservesActionOrder()
		{
			var menu = Parse(Text("X-KDE-Submenu=Images\nX-KDE-Submenu[fr]=Photos\nX-KDE-Priority=TopLevel\nTerminal=true\n")
				.Replace("Actions=run;", "Actions=run;second;") + "[Desktop Action second]\nName=Second\nExec=other %U\n");
			Assert.IsNotNull(menu);
			CollectionAssert.AreEqual(new[] { "run", "second" }, menu.Actions.Select(a => a.ActionId).ToArray());
			Assert.AreEqual("Exécuter", menu.Actions[0].Application.Name);
			Assert.AreEqual("Photos", menu.Actions[0].Submenu);
			Assert.AreEqual("TopLevel", menu.Actions[0].Priority);
			Assert.IsTrue(menu.Actions[0].Application.RunInTerminal);
		}

		[TestMethod]
		[DataRow("Type=Service", "Type=Application")]
		[DataRow("Actions=run;", "Actions=run;run;")]
		[DataRow("Actions=run;", "Actions=missing;")]
		[DataRow("Type=Service", "Type=Service\nType=Service")]
		[DataRow("Exec=tool %F", "Exec=tool %F\nExec=evil")]
		[DataRow("Exec=tool %F", "Exec[fr]=evil\nExec=tool %F")]
		[DataRow("Exec=tool %F", "Exec=tool\\nargument")]
		[DataRow("Name=Run", "Name=R\0un")]
		[DataRow("[Desktop Action run]", "[Desktop Entry]")]
		[DataRow("[Desktop Action run]", "[Desktop Action run]\n[Desktop Action run]")]
		public void Parse_RejectsAmbiguousOrInvalidEntries(string before, string after) => Assert.IsNull(Parse(Text().Replace(before, after)));

		[TestMethod]
		public void Filter_RequiresEveryMimeAndProtocol_AndHonorsUrlCounts()
		{
			using var fx = new XdgFixture();
			var h = new MimeHierarchy(fx.Directories);
			var menu = Parse(Text("X-KDE-MinNumberOfUrls=2\nX-KDE-RequiredNumberOfUrls=2,4,6\nX-KDE-Protocols=file,smb\n"))!;
			Assert.IsTrue(menu.Matches(["/a.png", "smb://host/b.jpg"], ["image/png", "image/jpeg"], h));
			Assert.IsFalse(menu.Matches(["/a.png"], ["image/png"], h));
			Assert.IsFalse(menu.Matches(["/a.png", "/b.txt"], ["image/png", "text/plain"], h));
			Assert.IsFalse(menu.Matches(["/a.png", "https://host/b.jpg"], ["image/png", "image/jpeg"], h));
			Assert.IsFalse(menu.Matches(["/a.png", "/b.png", "/c.png"], ["image/png", "image/png", "image/png"], h));
			Assert.IsFalse(menu.Matches([], [], h));
		}

		[TestMethod]
		public void Filter_LegacyServiceTypesAliasesParentsAndDirectories()
		{
			using var fx = new XdgFixture();
			fx.Write("usr-share/mime/aliases", "image/x-alias image/png\n");
			fx.Write("usr-share/mime/subclasses", "application/x-custom application/zip\n");
			var h = new MimeHierarchy(fx.Directories);
			Assert.IsTrue(Parse(Text())!.Matches(["/x"], ["image/x-alias"], h));
			var legacy = Parse(Text().Replace("MimeType=image/*;", "X-KDE-ServiceTypes=KonqPopupMenu/Plugin,application/zip;"))!;
			Assert.IsTrue(legacy.Matches(["/x"], ["application/x-custom"], h));
			var allFiles = Parse(Text().Replace("image/*", "application/octet-stream"))!;
			Assert.IsTrue(allFiles.Matches(["/x"], ["text/plain"], h));
			Assert.IsFalse(allFiles.Matches(["/dir"], ["inode/directory"], h));
			Assert.IsTrue(Parse(Text().Replace("image/*", "inode/directory"))!.Matches(["/dir"], ["inode/directory"], h));
		}

		[TestMethod]
		[DataRow("X-KDE-MinNumberOfUrls=-1\n")]
		[DataRow("X-KDE-MinNumberOfUrls=oops\n")]
		[DataRow("X-KDE-RequiredNumberOfUrls=1,no\n")]
		[DataRow("X-KDE-MinNumberOfUrls=3\nX-KDE-MaxNumberOfUrls=2\n")]
		public void Parse_RefusesMalformedConstraints(string extra) => Assert.IsNull(Parse(Text(extra)));

		[TestMethod]
		public async Task Scan_UserOverridesSystem_AndFindsOldDirectories_AndOrdersPriority()
		{
			using var fx = new XdgFixture();
			fx.Write("usr-share/mime/globs2", "50:image/png:*.png\n");
			fx.Write("data/kio/servicemenus/a.desktop", Text("Hidden=true\n"));
			fx.Write("usr-share/kio/servicemenus/a.desktop", Text());
			fx.Write("data/kservices5/ServiceMenus/b.desktop", Text("X-KDE-Priority=TopLevel\n"));
			fx.Write("usr-share/kio/servicemenus/c.desktop", Text());
			var actions = await Service(fx).GetActionsAsync(["/a.png"]);
			Assert.AreEqual(2, actions.Count);
			Assert.AreEqual("b.desktop", Path.GetFileName(actions[0].Application.DesktopFilePath));
			Assert.AreEqual("c.desktop", Path.GetFileName(actions[1].Application.DesktopFilePath));
		}

		[TestMethod]
		public async Task Scan_RejectsOversizedFilesSymlinksAndExcessiveSelection()
		{
			using var fx = new XdgFixture();
			fx.Write("usr-share/mime/globs2", "50:image/png:*.png\n");
			fx.Write("data/kio/servicemenus/big.desktop", Text() + new string('#', DesktopEntryDisplay.MaxFileSize));
			var original = fx.Write("home/original.desktop", Text());
			File.CreateSymbolicLink(Path.Combine(fx.DataHome, "kio/servicemenus/link.desktop"), original);
			Assert.AreEqual(0, (await Service(fx).GetActionsAsync(["/a.png"])).Count);
			Assert.AreEqual(0, (await Service(fx).GetActionsAsync(Enumerable.Repeat("/a.png", LinuxServiceMenuService.MaxSelection + 1).ToArray())).Count);
		}

		[TestMethod]
		public async Task Scan_CapsWorkAndResults()
		{
			using var fx = new XdgFixture();
			fx.Write("usr-share/mime/globs2", "50:image/png:*.png\n");
			for (var i = 0; i < LinuxServiceMenuService.MaxActions + 5; i++)
				fx.Write($"data/kio/servicemenus/{i}.desktop", Text());
			Assert.AreEqual(LinuxServiceMenuService.MaxActions, (await Service(fx).GetActionsAsync(["/a.png"])).Count);
			Directory.Delete(Path.Combine(fx.DataHome, "kio/servicemenus"), true);
			for (var i = 0; i < LinuxServiceMenuService.MaxScannedFiles + 1; i++)
				fx.Write($"data/kio/servicemenus/{i}.desktop", "invalid");
			fx.Write("usr-share/kio/servicemenus/valid.desktop", Text());
			Assert.AreEqual(0, (await Service(fx).GetActionsAsync(["/a.png"])).Count);
		}

		[TestMethod]
		public void Plan_PinsCodeBeforeDialog_AndRefusesChangedOrUnsupportedActions()
		{
			using var fx = new XdgFixture();
			var path = fx.Write("data/kio/servicemenus/run.desktop", Text());
			var action = ServiceMenuParser.ParseStrict(Text().Split('\n'), path, CultureInfo.InvariantCulture)!.Actions[0];
			var plan = ServiceMenuLaunchPlan.Create(action, ["/a.png", "/b.png"], CultureInfo.InvariantCulture);
			Assert.IsNotNull(plan);
			CollectionAssert.AreEqual(new[] { "tool", "/a.png", "/b.png" }, plan.Invocations[0].ToArray());
			Assert.IsTrue(plan.Identity.StillMatches(path));
			File.WriteAllText(path, Text(exec: "evil %F"));
			Assert.IsFalse(plan.Identity.StillMatches(path));
			Assert.IsNull(ServiceMenuLaunchPlan.Create(action, ["/a.png"], CultureInfo.InvariantCulture));
			File.WriteAllText(path, Text(exec: "sh -c '%F'"));
			var shell = ServiceMenuParser.ParseStrict(File.ReadAllLines(path), path, CultureInfo.InvariantCulture)!.Actions[0];
			Assert.IsNull(ServiceMenuLaunchPlan.Create(shell, ["/a.png"], CultureInfo.InvariantCulture));
		}

		[TestMethod]
		[DataRow("%f", false, true)]
		[DataRow("%F", false, false)]
		[DataRow("%u", true, true)]
		[DataRow("%U", true, false)]
		public void Expansion_TargetsRemainLiteralArgvElements(string field, bool urls, bool single)
		{
			string[] targets = ["/tmp/a 'quote' \"double\" $(touch bad);\nfile.png", "/tmp/-b\\file.png"];
			var app = new DesktopApplication("run", "Run", "tool \"" + field + "\"", "/menu.desktop");
			var commands = DesktopExecExpander.ExpandServiceMenu(app, targets);
			Assert.AreEqual(single ? 2 : 1, commands.Count);
			var expected = urls ? targets.Select(DesktopExecExpander.ToUri).ToArray() : targets;
			CollectionAssert.AreEqual(expected, commands.SelectMany(a => a.Skip(1)).ToArray());
			Assert.IsNotNull(DisplaySanitizer.FullArguments(commands[0]));
		}

		[TestMethod]
		[DataRow("sh -c 'tool %F'")]
		[DataRow("env bash -c 'tool'")]
		[DataRow("tool %F | other")]
		[DataRow("tool $(id)")]
		[DataRow("tool `id`")]
		[DataRow("tool > output")]
		[DataRow("env -S 'bash -c code'")]
		[DataRow("tool *.txt")]
		[DataRow("tool ~")]
		[DataRow("tool %f %U")]
		[DataRow("tool --input=%f")]
		[DataRow("tool '%F suffix'")]
		[DataRow("tool %D")]
		[DataRow("tool 'unterminated")]
		public void Expansion_RefusesShellsSyntaxAndEmbeddedTargetCodes(string exec) =>
			Assert.AreEqual(0, DesktopExecExpander.ExpandServiceMenu(new DesktopApplication("run", "Run", exec, "/menu.desktop"), ["/a.png"]).Count);

		[TestMethod]
		public void Plan_RefusesArgvThatCannotBeDisplayedInFull()
		{
			using var fx = new XdgFixture();
			var path = fx.Write("data/kio/servicemenus/run.desktop", Text());
			var action = ServiceMenuParser.ParseStrict(Text().Split('\n'), path, CultureInfo.InvariantCulture)!.Actions[0];
			Assert.IsNull(ServiceMenuLaunchPlan.Create(action, ["/" + new string('a', DisplaySanitizer.MaxExecutedCharacters)], CultureInfo.InvariantCulture));
			var remote = new DesktopApplication("run", "Run", "tool %F", path);
			Assert.AreEqual(0, DesktopExecExpander.ExpandServiceMenu(remote, ["smb://host/file.png"]).Count);
		}

		[TestMethod]
		public void Expansion_LiteralsAndMetadata_AreNotReparsed()
		{
			var app = new DesktopApplication("run", "Name $()", "tool %%f %c %k %i %F", "/file with spaces.desktop", "an icon");
			var argv = DesktopExecExpander.ExpandServiceMenu(app, ["file:///tmp/a%20b.png"])[0];
			CollectionAssert.AreEqual(new[] { "tool", "%f", "Name $()", "/file with spaces.desktop", "--icon", "an icon", "/tmp/a b.png" }, argv.ToArray());
		}
	}
}
