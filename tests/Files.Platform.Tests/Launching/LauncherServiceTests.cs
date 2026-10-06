// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Launching;
using Files.Platform.Linux.Mime;
using Files.Platform.Tests.Mime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Launching
{
	[TestClass]
	public sealed class LauncherServiceTests
	{
		private sealed class RecordingStarter : IProcessStarter
		{
			public List<ProcessLaunch> Launches { get; } = [];
			public bool Fail { get; set; }

			public Task StartDetachedAsync(ProcessLaunch launch, CancellationToken cancellationToken = default)
			{
				if (Fail)
					throw new FileNotFoundException("missing", launch.FileName);

				Launches.Add(launch);
				return Task.CompletedTask;
			}
		}

		private static (LinuxLauncherService Service, RecordingStarter Starter) Create(XdgFixture fx, Dictionary<string, string?>? env = null, params string[] executables)
		{
			fx.Write("usr-share/mime/globs2", "50:text/plain:*.txt\n50:image/png:*.png\n50:application/vnd.appimage:*.AppImage\n50:application/x-desktop:*.desktop\n50:application/x-shellscript:*.sh\n");
			var culture = CultureInfo.InvariantCulture;
			var locator = new FakeLocator(executables);
			var starter = new RecordingStarter();
			var service = new LinuxLauncherService(
				new LinuxMimeTypeService(fx.Directories, culture),
				new LinuxApplicationRegistry(fx.Directories, culture, locator),
				starter,
				new TerminalResolver(locator, name => env is not null && env.TryGetValue(name, out var v) ? v : null),
				new RootTerminalResolver(locator, name => env is not null && env.TryGetValue(name, out var v) ? v : null, () => false));
			return (service, starter);
		}

		[TestMethod]
		public async Task Open_GroupsFilesByDefaultApp_AndHonorsFieldCodes()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "multi.desktop", "Multi", "multi %F");
			fx.WriteDesktop("usr-share", "single.desktop", "Single", "single %f");
			fx.Write("config/mimeapps.list", "[Default Applications]\ntext/plain=multi.desktop;\nimage/png=single.desktop;\n");
			var (svc, starter) = Create(fx);

			var ok = await svc.OpenAsync(["/a.txt", "/b.png", "/c.txt", "/d.png"]);

			Assert.IsTrue(ok);
			Assert.AreEqual(3, starter.Launches.Count);
			Assert.AreEqual("multi", starter.Launches[0].FileName);
			CollectionAssert.AreEqual(new[] { "/a.txt", "/c.txt" }, starter.Launches[0].Arguments.ToArray());
			CollectionAssert.AreEqual(new[] { "/b.png" }, starter.Launches[1].Arguments.ToArray());
			CollectionAssert.AreEqual(new[] { "/d.png" }, starter.Launches[2].Arguments.ToArray());
		}

		[TestMethod]
		public async Task Open_ExecutableMimeTypes_AreNeverHandedToAHandler()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "any.desktop", "Any", "any %f");
			fx.Write("config/mimeapps.list", "[Default Applications]\napplication/vnd.appimage=any.desktop;\napplication/x-desktop=any.desktop;\n");
			var (svc, starter) = Create(fx);

			Assert.IsFalse(await svc.OpenAsync(["/x/tool.AppImage"]));
			Assert.IsFalse(await svc.OpenAsync(["/x/evil.desktop"]));
			Assert.AreEqual(0, starter.Launches.Count);
		}

		[TestMethod]
		public async Task Open_ExecBitFileWithoutDefaultApp_DoesNotFallBackToXdgOpen_AlsoThroughSymlink()
		{
			using var fx = new XdgFixture();
			var (svc, starter) = Create(fx);
			var script = fx.Write("home/run.sh", "echo hi\n");
			if (OperatingSystem.IsWindows()) return;
			File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			var link = Path.Combine(fx.Home, "link.sh");
			File.CreateSymbolicLink(link, script);

			Assert.IsFalse(await svc.OpenAsync([script]));
			Assert.IsFalse(await svc.OpenAsync([link]));
			Assert.AreEqual(0, starter.Launches.Count);

			if (OperatingSystem.IsWindows()) return;
			File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite);
			Assert.IsTrue(await svc.OpenAsync([script]));
			Assert.AreEqual("xdg-open", starter.Launches.Single().FileName);
		}

		[TestMethod]
		public async Task LaunchUri_RefusesFileUris()
		{
			using var fx = new XdgFixture();
			var (svc, starter) = Create(fx);

			Assert.IsFalse(await svc.LaunchUriAsync(new Uri("file:///tmp/x")));
			Assert.AreEqual(0, starter.Launches.Count);
		}

		[TestMethod]
		public async Task RunCommand_StartsExactlyTheDisplayedArgv_PlainAndInTerminal()
		{
			using var fx = new XdgFixture();
			var (svc, starter) = Create(fx, new Dictionary<string, string?> { ["TERMINAL"] = "xterm" }, "xterm");
			var app = new Files.Platform.Abstractions.Mime.DesktopApplication("x.desktop", "X", "/bin/echo \"a b\" \\\\s 'c d' %c", "/x.desktop");
			var argv = DesktopExecExpander.Expand(app, [])[0];

			Assert.IsTrue(await svc.RunCommandAsync(argv, false));
			Assert.AreEqual(argv[0], starter.Launches[0].FileName);
			CollectionAssert.AreEqual(argv.Skip(1).ToArray(), starter.Launches[0].Arguments.ToArray());

			Assert.IsTrue(await svc.RunCommandAsync(argv, true));
			CollectionAssert.IsSubsetOf(argv.ToArray(), new[] { starter.Launches[1].FileName }.Concat(starter.Launches[1].Arguments).ToArray());
		}

		[TestMethod]
		public async Task Open_WithoutDefaultApp_FallsBackToXdgOpen()
		{
			using var fx = new XdgFixture();
			var (svc, starter) = Create(fx);

			Assert.IsTrue(await svc.OpenAsync(["/a.txt"]));

			Assert.AreEqual("xdg-open", starter.Launches.Single().FileName);
			CollectionAssert.AreEqual(new[] { "/a.txt" }, starter.Launches.Single().Arguments.ToArray());
		}

		[TestMethod]
		[DataRow("konsole")]
		[DataRow("gnome-terminal")]
		[DataRow("xdg-terminal-exec")]
		[DataRow("xterm")]
		[DataRow("ghostty")]
		public async Task RootTerminal_PreservesLiteralDirectoryAndUsesPreferredTool(string terminal)
		{
			using var fx = new XdgFixture();
			var folder = Path.Combine(fx.Home, "a 'quoted' $(touch nope); dir");
			Directory.CreateDirectory(folder);
			var (service, starter) = Create(fx, new() { ["TERMINAL"] = terminal }, terminal, "run0", "sudo", "pkexec");
			Assert.IsTrue(service.CanOpenTerminalAsRoot);
			Assert.IsTrue(await service.OpenTerminalAsRootAsync(folder));
			var launch = starter.Launches.Single();
			Assert.AreEqual(terminal, launch.FileName);
			Assert.AreEqual(folder, launch.WorkingDirectory);
			CollectionAssert.AreEqual(new[] { "/usr/bin/run0", "--chdir=" + folder }, launch.Arguments.TakeLast(2).ToArray());
			Assert.IsFalse(launch.Arguments.Contains("-c"));
		}

		[TestMethod]
		public async Task RootTerminal_FallbacksRefusalsAndPackageGate()
		{
			using var fx = new XdgFixture();
			var (sudo, starter) = Create(fx, null, "konsole", "sudo", "pkexec");
			Assert.IsTrue(await sudo.OpenTerminalAsRootAsync(fx.Home));
			CollectionAssert.AreEqual(new[] { "--workdir", fx.Home, "-e", "/usr/bin/sudo", "-s" }, starter.Launches.Single().Arguments.ToArray());
			var (pkexec, pkStarter) = Create(fx, new() { ["SHELL"] = "/bin/zsh" }, "konsole", "pkexec", "/bin/zsh");
			Assert.IsTrue(await pkexec.OpenTerminalAsRootAsync(fx.Home));
			CollectionAssert.AreEqual(new[] { "/usr/bin/pkexec", "--keep-cwd", "/usr/bin//bin/zsh" }, pkStarter.Launches.Single().Arguments.TakeLast(3).ToArray());
			var (fallback, fallbackStarter) = Create(fx, new() { ["SHELL"] = "sh -c code" }, "konsole", "pkexec", "/bin/sh");
			Assert.IsTrue(await fallback.OpenTerminalAsRootAsync(fx.Home));
			Assert.AreEqual("/usr/bin//bin/sh", fallbackStarter.Launches.Single().Arguments.Last());
			Assert.IsFalse(await sudo.OpenTerminalAsRootAsync("relative"));
			Assert.IsFalse(await sudo.OpenTerminalAsRootAsync(fx.Home + "/missing"));
			Assert.IsFalse(await sudo.OpenTerminalAsRootAsync("file:///tmp"));
			var missingTool = Create(fx, null, "konsole");
			Assert.IsFalse(missingTool.Service.CanOpenTerminalAsRoot);
			Assert.IsFalse(await missingTool.Service.OpenTerminalAsRootAsync(fx.Home));
			Assert.AreEqual(0, missingTool.Starter.Launches.Count);
			var missingTerminal = Create(fx, null, "sudo");
			Assert.IsFalse(missingTerminal.Service.CanOpenTerminalAsRoot);
			Assert.IsFalse(await missingTerminal.Service.OpenTerminalAsRootAsync(fx.Home));
			Assert.AreEqual(0, missingTerminal.Starter.Launches.Count);
			var disabled = new RootTerminalResolver(new FakeLocator("run0", "sudo", "pkexec"), _ => null, () => true);
			Assert.IsNull(disabled.Resolve(fx.Home));
		}

		[TestMethod]
		public async Task Open_EmptyInput_ReturnsFalse()
		{
			using var fx = new XdgFixture();
			var (svc, starter) = Create(fx);

			Assert.IsFalse(await svc.OpenAsync([]));
			Assert.AreEqual(0, starter.Launches.Count);
		}

		[TestMethod]
		public async Task Open_StartFailure_ReturnsFalse()
		{
			using var fx = new XdgFixture();
			var (svc, starter) = Create(fx);
			starter.Fail = true;

			Assert.IsFalse(await svc.OpenAsync(["/a.txt"]));
		}

		[TestMethod]
		public async Task OpenWith_TerminalApp_IsWrappedInTerminal()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "vim.desktop", "Vim", "vim %F", "Terminal=true\n");
			var (svc, starter) = Create(fx, null, "konsole");
			var app = (await new LinuxApplicationRegistry(fx.Directories, CultureInfo.InvariantCulture, new FakeLocator()).GetApplicationAsync("vim.desktop"))!;

			Assert.IsTrue(await svc.OpenWithAsync(app, ["/a.txt"]));

			Assert.AreEqual("konsole", starter.Launches.Single().FileName);
			CollectionAssert.AreEqual(new[] { "-e", "vim", "/a.txt" }, starter.Launches.Single().Arguments.ToArray());
		}

		[TestMethod]
		public async Task OpenWith_TerminalApp_NoTerminal_ReturnsFalse()
		{
			using var fx = new XdgFixture();
			fx.WriteDesktop("usr-share", "vim.desktop", "Vim", "vim %F", "Terminal=true\n");
			var (svc, starter) = Create(fx);
			var app = (await new LinuxApplicationRegistry(fx.Directories, CultureInfo.InvariantCulture, new FakeLocator()).GetApplicationAsync("vim.desktop"))!;

			Assert.IsFalse(await svc.OpenWithAsync(app, ["/a.txt"]));
			Assert.AreEqual(0, starter.Launches.Count);
		}

		[TestMethod]
		public async Task LaunchUri_UsesXdgOpen()
		{
			using var fx = new XdgFixture();
			var (svc, starter) = Create(fx);

			Assert.IsTrue(await svc.LaunchUriAsync(new Uri("https://example.com/a b")));

			Assert.AreEqual("xdg-open", starter.Launches.Single().FileName);
			CollectionAssert.AreEqual(new[] { "https://example.com/a%20b" }, starter.Launches.Single().Arguments.ToArray());
		}

		[TestMethod]
		public async Task RunExecutable_PassesArgumentsAndWorkingDirectory()
		{
			using var fx = new XdgFixture();
			var (svc, starter) = Create(fx);

			Assert.IsTrue(await svc.RunExecutableAsync("/opt/tool", ["--x", "a b"], "/work"));

			var launch = starter.Launches.Single();
			Assert.AreEqual("/opt/tool", launch.FileName);
			CollectionAssert.AreEqual(new[] { "--x", "a b" }, launch.Arguments.ToArray());
			Assert.AreEqual("/work", launch.WorkingDirectory);
		}

		[TestMethod]
		public async Task OpenTerminal_PrefersTerminalEnv_ThenXdgTerminalExec_ThenKnownList()
		{
			using var fx = new XdgFixture();

			var (svc1, st1) = Create(fx, new() { ["TERMINAL"] = "kitty" }, "kitty", "konsole", "xdg-terminal-exec");
			Assert.IsTrue(await svc1.OpenTerminalAsync("/home/u"));
			Assert.AreEqual("kitty", st1.Launches.Single().FileName);
			CollectionAssert.AreEqual(new[] { "--directory", "/home/u" }, st1.Launches.Single().Arguments.ToArray());
			Assert.AreEqual("/home/u", st1.Launches.Single().WorkingDirectory);

			var (svc2, st2) = Create(fx, new() { ["TERMINAL"] = "missing-term" }, "konsole", "xdg-terminal-exec");
			await svc2.OpenTerminalAsync("/home/u");
			Assert.AreEqual("xdg-terminal-exec", st2.Launches.Single().FileName);

			var (svc3, st3) = Create(fx, null, "xterm", "alacritty");
			await svc3.OpenTerminalAsync("/home/u");
			Assert.AreEqual("alacritty", st3.Launches.Single().FileName);
			CollectionAssert.AreEqual(new[] { "--working-directory", "/home/u" }, st3.Launches.Single().Arguments.ToArray());

			var (svc4, st4) = Create(fx, new() { ["TERMINAL"] = "foot -a files" }, "foot");
			await svc4.OpenTerminalAsync("/home/u");
			CollectionAssert.AreEqual(new[] { "-a", "files", "--working-directory=/home/u" }, st4.Launches.Single().Arguments.ToArray());
		}

		[TestMethod]
		public async Task OpenTerminal_NoTerminalInstalled_ReturnsFalse()
		{
			using var fx = new XdgFixture();
			var (svc, starter) = Create(fx);

			Assert.IsFalse(await svc.OpenTerminalAsync("/home/u"));
			Assert.AreEqual(0, starter.Launches.Count);
		}
	}
}
