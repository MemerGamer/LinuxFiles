// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Elevation;
using Files.Platform.Linux.Elevation;
using Files.Platform.Linux.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Tests.SystemIntegration
{
	[TestClass]
	public sealed class ElevationServiceTests
	{
		private const uint Me = 1000;

		private sealed class FakeFs : IFileOwnershipInspector
		{
			public Dictionary<string, FileEntryInfo> Entries { get; } = new();

			public bool TryGetInfo(string path, out FileEntryInfo info) => Entries.TryGetValue(path, out info!);

			public void Dir(string path, uint owner = 0, UnixFileMode mode = (UnixFileMode)0b111_101_101)
				=> Entries[path] = new FileEntryInfo(true, false, owner, mode);

			public void File(string path, uint owner = Me, UnixFileMode mode = (UnixFileMode)0b110_100_100)
				=> Entries[path] = new FileEntryInfo(false, false, owner, mode);

			public void Link(string path)
				=> Entries[path] = new FileEntryInfo(false, true, Me, (UnixFileMode)0b111_111_111);

			/// <summary>root-owned /, /opt, /opt/app and /home/u owned by the user, with sources and a destination.</summary>
			public static FakeFs Standard()
			{
				var fs = new FakeFs();
				foreach (var dir in new[] { "/", "/opt", "/opt/app", "/etc" })
					fs.Dir(dir);
				fs.Dir("/home");
				fs.Dir("/home/u", Me);
				fs.File("/home/u/a.txt");
				fs.File("/home/u/b.txt");
				fs.File("/opt/app/old");
				return fs;
			}
		}

		private sealed class FakeTools(params string[] names) : ITrustedToolResolver
		{
			public string? Resolve(string name) => names.Contains(name) ? "/usr/bin/" + name : null;
		}

		private sealed class FakeRunner(int exitCode = 0, string error = "", Action<string[]>? effect = null) : IElevatedProcessRunner
		{
			public List<(string File, string[] Args)> Calls { get; } = [];

			public Task<(int ExitCode, string StandardError)> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
			{
				Calls.Add((fileName, arguments.ToArray()));
				if (exitCode == 0)
					effect?.Invoke(arguments.ToArray());
				return Task.FromResult((exitCode, error));
			}
		}

		private static readonly FakeTools AllTools = new("pkexec", "rm", "cp", "mv");

		private static PkexecElevationService Create(FakeFs fs, FakeRunner runner, FakeTools? tools = null)
			=> new(new ElevationPathChecker(fs, Me), tools ?? AllTools, runner);

		[TestMethod]
		public async Task DeleteIsOnePkexecWithAllPathsAndNoShell()
		{
			var fs = FakeFs.Standard();
			fs.File("/home/u/dir; rm -rf ~");
			var runner = new FakeRunner(effect: args => { fs.Entries.Remove("/home/u/a.txt"); fs.Entries.Remove("/home/u/dir; rm -rf ~"); });
			var service = Create(fs, runner);

			var plan = service.PlanDelete(["/home/u/a.txt", "/home/u/dir; rm -rf ~"]);
			Assert.IsNotNull(plan.Plan);
			var result = await service.RunAsync(plan.Plan);

			Assert.IsTrue(result.Succeeded, result.Error);
			Assert.AreEqual(1, runner.Calls.Count);
			Assert.AreEqual("/usr/bin/pkexec", runner.Calls[0].File);
			CollectionAssert.AreEqual(new[] { "/usr/bin/rm", "-rf", "--", "/home/u/a.txt", "/home/u/dir; rm -rf ~" }, runner.Calls[0].Args);
		}

		[TestMethod]
		public void CopyUsesNoClobberAndNoOwnershipAndRefusesExistingOrPlantedTargets()
		{
			var fs = FakeFs.Standard();
			var service = Create(fs, new FakeRunner());

			var plan = service.PlanCopy(["/home/u/a.txt"], "/opt/app");
			Assert.IsNotNull(plan.Plan);
			CollectionAssert.AreEqual(new[] { "-R", "-P", "-n", "--preserve=timestamps,links", "--", "/home/u/a.txt", "/opt/app/" }, plan.Plan.Commands[0].Arguments.ToArray());

			fs.Link("/opt/app/a.txt");
			Assert.IsNull(service.PlanCopy(["/home/u/a.txt"], "/opt/app").Plan, "a planted symlink must not be written through");
			fs.Entries.Remove("/opt/app/a.txt");
			fs.File("/opt/app/a.txt");
			Assert.IsNull(service.PlanCopy(["/home/u/a.txt"], "/opt/app").Plan);
		}

		[TestMethod]
		[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
		public void CopyRefusesSetuidSources()
		{
			var root = Path.Combine(Path.GetTempPath(), "felev-" + Guid.NewGuid().ToString("N")[..8]);
			Directory.CreateDirectory(Path.Combine(root, "src"));
			Directory.CreateDirectory(Path.Combine(root, "dst"));
			try
			{
				var file = Path.Combine(root, "src", "tool");
				File.WriteAllText(file, "x");
				var service = new PkexecElevationService(new ElevationPathChecker(new StatxFileOwnershipInspector(), ProcessIdentityNative.CurrentUserId), AllTools, new FakeRunner());
				Assert.IsNotNull(service.PlanCopy([Path.Combine(root, "src")], Path.Combine(root, "dst")).Plan);

				File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.SetUser);
				Assert.IsNull(service.PlanCopy([Path.Combine(root, "src")], Path.Combine(root, "dst")).Plan);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[TestMethod]
		public void RefusesDirectoriesOthersCouldSwap()
		{
			var fs = FakeFs.Standard();
			var service = Create(fs, new FakeRunner());

			fs.Dir("/home/u", owner: 2000);
			Assert.IsNull(service.PlanDelete(["/home/u/a.txt"]).Plan, "another user owns an ancestor");
			fs.Dir("/home/u", Me, (UnixFileMode)0b111_111_101);
			Assert.IsNull(service.PlanDelete(["/home/u/a.txt"]).Plan, "group-writable ancestor");
			fs.Dir("/home/u", Me);
			Assert.IsNotNull(service.PlanDelete(["/home/u/a.txt"]).Plan);
			fs.Dir("/home", 0, (UnixFileMode)0b111_111_111 | UnixFileMode.StickyBit);
			Assert.IsNotNull(service.PlanDelete(["/home/u/a.txt"]).Plan, "root-owned sticky directories are safe");
		}

		[TestMethod]
		public async Task RenameRefusesPathsExistingTargetsAndNoOpMv()
		{
			var fs = FakeFs.Standard();
			var runner = new FakeRunner();
			var service = Create(fs, runner);

			foreach (var name in new[] { "../b", "b/c", "..", ".", "" })
				Assert.IsNull(service.PlanRename("/opt/app/old", name).Plan, name);
			Assert.IsNull(service.PlanRename("/", "x").Plan);
			Assert.IsNull(service.PlanRename("/opt/app/old", "old").Plan, "existing target");

			var plan = service.PlanRename("/opt/app/old", "new; name").Plan!;
			CollectionAssert.AreEqual(new[] { "-n", "-T", "--", "/opt/app/old", "/opt/app/new; name" }, plan.Commands[0].Arguments.ToArray());

			// mv -n exits 0 when it skips; the missing effect must be reported
			var result = await service.RunAsync(plan);
			Assert.IsFalse(result.Succeeded);
			Assert.AreEqual(1, runner.Calls.Count);
		}

		[TestMethod]
		public async Task RunsExactlyTheConfirmedPlanAndRefusesForgedOnes()
		{
			var fs = FakeFs.Standard();
			var runner = new FakeRunner();
			var service = Create(fs, runner);

			var plan = service.PlanDelete(["/home/u/a.txt"]).Plan!;
			var forged = plan with { Commands = [new ElevatedCommand("/usr/bin/rm", ["-rf", "--", "/etc"])] };
			var swappedProgram = plan with { Commands = [new ElevatedCommand("/tmp/evil", plan.Commands[0].Arguments)] };
			var staleSources = plan with { Sources = ["/home/u/b.txt"] };

			Assert.IsFalse((await service.RunAsync(forged)).Succeeded);
			Assert.IsFalse((await service.RunAsync(swappedProgram)).Succeeded);
			Assert.IsFalse((await service.RunAsync(staleSources)).Succeeded);
			Assert.AreEqual(0, runner.Calls.Count);
		}

		[TestMethod]
		public async Task MoveIsCopyThenDeleteAndDoesNotDeleteUnverifiedCopies()
		{
			var fs = FakeFs.Standard();
			var runner = new FakeRunner();
			var service = Create(fs, runner);

			var plan = service.PlanMove(["/home/u/a.txt"], "/opt/app").Plan!;
			Assert.AreEqual(2, plan.Commands.Count);
			Assert.AreEqual("/usr/bin/cp", plan.Commands[0].Program);
			Assert.AreEqual("/usr/bin/rm", plan.Commands[1].Program);

			var result = await service.RunAsync(plan);
			Assert.IsFalse(result.Succeeded);
			Assert.AreEqual(1, runner.Calls.Count, "the originals must survive when the copy did not happen");

			var effect = new FakeRunner(effect: args =>
			{
				if (args[0] == "/usr/bin/cp") fs.File("/opt/app/a.txt");
				else fs.Entries.Remove("/home/u/a.txt");
			});
			var ok = await Create(fs, effect).RunAsync(plan);
			Assert.IsTrue(ok.Succeeded, ok.Error);
			Assert.AreEqual(2, effect.Calls.Count);
		}

		[TestMethod]
		public async Task ReportsDismissedPromptSeparatelyFromStartFailureAndMissingTools()
		{
			var fs = FakeFs.Standard();
			var plan = Create(fs, new FakeRunner()).PlanDelete(["/home/u/a.txt"]).Plan!;

			var dismissed = await Create(fs, new FakeRunner(126)).RunAsync(plan);
			Assert.IsTrue(dismissed.WasDismissed);

			var couldNotStart = await Create(fs, new FakeRunner(127, "no agent")).RunAsync(plan);
			Assert.IsFalse(couldNotStart.WasDismissed);
			Assert.AreEqual("no agent", couldNotStart.Error);

			var noRm = Create(fs, new FakeRunner(), new FakeTools("pkexec", "cp", "mv"));
			Assert.IsFalse(noRm.IsAvailable);
			StringAssert.Contains(noRm.PlanDelete(["/home/u/a.txt"]).Refusal, "rm");
		}

		[TestMethod]
		public void ToolsComeOnlyFromTrustedSystemLocations()
		{
			var fs = new FakeFs();
			fs.Dir("/");
			fs.Dir("/usr");
			fs.Dir("/usr/bin");
			fs.Dir("/bin");
			var resolver = new SystemToolResolver(new ElevationPathChecker(fs, Me));

			fs.File("/usr/bin/rm", 0, (UnixFileMode)0b111_101_101);
			Assert.AreEqual("/usr/bin/rm", resolver.Resolve("rm"));

			fs.File("/usr/bin/rm", Me, (UnixFileMode)0b111_101_101);
			Assert.IsNull(resolver.Resolve("rm"), "not owned by root");
			fs.File("/usr/bin/rm", 0, (UnixFileMode)0b111_111_101);
			Assert.IsNull(resolver.Resolve("rm"), "group-writable");
			Assert.IsNull(resolver.Resolve("chown"), "not in the allowlist");
			Assert.IsNull(resolver.Resolve("../tmp/evil"));
		}

		[TestMethod]
		public void CanonicalizeResolvesSymlinks()
		{
			var root = Path.Combine(Path.GetTempPath(), "fcanon-" + Guid.NewGuid().ToString("N")[..8]);
			Directory.CreateDirectory(Path.Combine(root, "real"));
			try
			{
				Directory.CreateSymbolicLink(Path.Combine(root, "link"), Path.Combine(root, "real"));
				var checker = new ElevationPathChecker(new StatxFileOwnershipInspector(), ProcessIdentityNative.CurrentUserId);
				Assert.AreEqual(Path.Combine(root, "real", "x"), checker.Canonicalize(Path.Combine(root, "link", "x")));
				Assert.AreEqual(Path.Combine(root, "real"), checker.Canonicalize(Path.Combine(root, "link", "..", "link")));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}
	}
}
