// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Launching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Launching
{
	[TestClass]
	public sealed class DetachedProcessStarterTests
	{
		private sealed class TestLocator(bool useSetsid) : IExecutableLocator
		{
			private readonly PathExecutableLocator inner = new();
			public string? Locate(string command) => command == "setsid" && !useSetsid ? null : inner.Locate(command);
		}

		[TestMethod]
		[DataRow(true)]
		[DataRow(false)]
		public async Task Launch_IsolatesStdioAndPreservesArguments(bool useSetsid)
		{
			if (!OperatingSystem.IsLinux())
				Assert.Inconclusive("Requires Linux descriptors.");
			if (useSetsid && new PathExecutableLocator().Locate("setsid") is null)
				Assert.Inconclusive("Requires setsid.");

			var directory = Path.Combine(Path.GetTempPath(), "files-detached-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				var script = Path.Combine(directory, "probe ' $().sh");
				var result = Path.Combine(directory, "result");
				// Inspect the launched shell's descriptors before redirecting the report to a file.
				await File.WriteAllTextAsync(script,
					"#!/bin/sh\n" +
					"a=$(readlink /proc/$$/fd/0)\nb=$(readlink /proc/$$/fd/1)\nc=$(readlink /proc/$$/fd/2)\n" +
					"printf '%s\\n' \"$a\" \"$b\" \"$c\" \"$PWD\" \"$@\" > result.tmp\n" +
					"mv result.tmp result\n");
				File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
				string[] arguments = ["a b", "", "$(touch injected)", "`touch injected`", "'\"; & *", "--option"];

				await new DetachedProcessStarter(new TestLocator(useSetsid))
					.StartDetachedAsync(new ProcessLaunch(script, arguments, directory));

				var deadline = DateTime.UtcNow.AddSeconds(10);
				while (!File.Exists(result) && DateTime.UtcNow < deadline)
					await Task.Delay(20);
				Assert.IsTrue(File.Exists(result), "The descriptor probe did not finish.");
				CollectionAssert.AreEqual(new string[] { "/dev/null", "/dev/null", "/dev/null", directory,
					arguments[0], arguments[1], arguments[2], arguments[3], arguments[4], arguments[5] },
					await File.ReadAllLinesAsync(result));
				Assert.IsFalse(File.Exists(Path.Combine(directory, "injected")));
			}
			finally
			{
				Directory.Delete(directory, recursive: true);
			}
		}
	}
}
