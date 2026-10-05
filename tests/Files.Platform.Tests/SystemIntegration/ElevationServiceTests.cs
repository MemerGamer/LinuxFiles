// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Elevation;
using Files.Platform.Tests.Mime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Tests.SystemIntegration
{
	[TestClass]
	public sealed class ElevationServiceTests
	{
		private sealed class FakeRunner(int exitCode = 0, string error = "") : IElevatedProcessRunner
		{
			public List<(string File, string[] Args)> Calls { get; } = [];

			public Task<(int ExitCode, string StandardError)> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
			{
				Calls.Add((fileName, arguments.ToArray()));
				return Task.FromResult((exitCode, error));
			}
		}

		[TestMethod]
		public async Task DeleteRunsRmThroughPkexecWithoutShell()
		{
			var runner = new FakeRunner();
			var service = new PkexecElevationService(new FakeLocator("pkexec", "rm", "cp"), runner);

			var result = await service.DeleteAsync("/root-owned/dir; rm -rf ~");

			Assert.IsTrue(result.Succeeded);
			Assert.AreEqual(1, runner.Calls.Count);
			Assert.AreEqual("/usr/bin/pkexec", runner.Calls[0].File);
			CollectionAssert.AreEqual(new[] { "/usr/bin/rm", "-rf", "--", "/root-owned/dir; rm -rf ~" }, runner.Calls[0].Args);
		}

		[TestMethod]
		public async Task CopyTargetsDestinationFolder()
		{
			var runner = new FakeRunner();
			var service = new PkexecElevationService(new FakeLocator("pkexec", "rm", "cp"), runner);

			await service.CopyAsync("/home/u/a.txt", "/opt/app");

			CollectionAssert.AreEqual(new[] { "/usr/bin/cp", "-a", "--", "/home/u/a.txt", "/opt/app/" }, runner.Calls[0].Args);
		}

		[TestMethod]
		public async Task RefusesRootRelativeAndEmptyPaths()
		{
			var runner = new FakeRunner();
			var service = new PkexecElevationService(new FakeLocator("pkexec", "rm", "cp"), runner);

			Assert.IsFalse((await service.DeleteAsync("/")).Succeeded);
			Assert.IsFalse((await service.DeleteAsync("/a/..")).Succeeded);
			Assert.IsFalse((await service.DeleteAsync("relative")).Succeeded);
			Assert.IsFalse((await service.DeleteAsync("")).Succeeded);
			Assert.IsFalse((await service.CopyAsync("rel", "/opt")).Succeeded);
			Assert.AreEqual(0, runner.Calls.Count);
		}

		[TestMethod]
		public async Task ReportsDismissedPromptAndMissingPkexec()
		{
			var dismissed = await new PkexecElevationService(new FakeLocator("pkexec", "rm"), new FakeRunner(126)).DeleteAsync("/x/y");
			Assert.IsFalse(dismissed.Succeeded);
			Assert.IsTrue(dismissed.WasDismissed);

			var failed = await new PkexecElevationService(new FakeLocator("pkexec", "rm"), new FakeRunner(1, "boom")).DeleteAsync("/x/y");
			Assert.IsFalse(failed.WasDismissed);
			Assert.AreEqual("boom", failed.Error);

			var missing = new PkexecElevationService(new FakeLocator("rm"), new FakeRunner());
			Assert.IsFalse(missing.IsAvailable);
			Assert.IsFalse((await missing.DeleteAsync("/x/y")).Succeeded);
		}

		[TestMethod]
		public async Task MoveAndRenameUseMvWithoutClobberingAndPlansMatchWhatRuns()
		{
			var runner = new FakeRunner();
			var service = new PkexecElevationService(new FakeLocator("pkexec", "mv"), runner);

			await service.MoveAsync("/home/u/a.txt", "/opt/app");
			await service.RenameAsync("/opt/app/old name", "new; name");

			CollectionAssert.AreEqual(new[] { "/usr/bin/mv", "-n", "--", "/home/u/a.txt", "/opt/app/" }, runner.Calls[0].Args);
			CollectionAssert.AreEqual(new[] { "/usr/bin/mv", "-n", "-T", "--", "/opt/app/old name", "/opt/app/new; name" }, runner.Calls[1].Args);
			Assert.AreEqual("pkexec /usr/bin/mv -n -- /home/u/a.txt /opt/app/", service.PlanMove("/home/u/a.txt", "/opt/app")!.DisplayText);
		}

		[TestMethod]
		public async Task RenameRefusesPathsAsNames()
		{
			var runner = new FakeRunner();
			var service = new PkexecElevationService(new FakeLocator("pkexec", "mv"), runner);

			Assert.IsFalse((await service.RenameAsync("/opt/a", "../b")).Succeeded);
			Assert.IsFalse((await service.RenameAsync("/opt/a", "b/c")).Succeeded);
			Assert.IsFalse((await service.RenameAsync("/opt/a", "..")).Succeeded);
			Assert.IsFalse((await service.RenameAsync("/opt/a", "")).Succeeded);
			Assert.IsFalse((await service.RenameAsync("/", "x")).Succeeded);
			Assert.IsFalse((await service.MoveAsync("/", "/opt")).Succeeded);
			Assert.IsNull(service.PlanRename("rel", "x"));
			Assert.AreEqual(0, runner.Calls.Count);
		}
	}
}
