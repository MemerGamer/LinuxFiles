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
			fx.Write("usr-share/mime/globs2", "50:text/plain:*.txt\n50:image/png:*.png\n");
			var culture = CultureInfo.InvariantCulture;
			var locator = new FakeLocator(executables);
			var starter = new RecordingStarter();
			var service = new LinuxLauncherService(
				new LinuxMimeTypeService(fx.Directories, culture),
				new LinuxApplicationRegistry(fx.Directories, culture, locator),
				starter,
				new TerminalResolver(locator, name => env is not null && env.TryGetValue(name, out var v) ? v : null));
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
		public async Task Open_WithoutDefaultApp_FallsBackToXdgOpen()
		{
			using var fx = new XdgFixture();
			var (svc, starter) = Create(fx);

			Assert.IsTrue(await svc.OpenAsync(["/a.txt"]));

			Assert.AreEqual("xdg-open", starter.Launches.Single().FileName);
			CollectionAssert.AreEqual(new[] { "/a.txt" }, starter.Launches.Single().Arguments.ToArray());
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
