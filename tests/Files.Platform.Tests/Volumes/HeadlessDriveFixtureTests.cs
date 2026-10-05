// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Volumes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;

namespace Files.Platform.Tests.Volumes
{
	[TestClass]
	public sealed class HeadlessDriveFixtureTests
	{
		private static string NewRoot()
		{
			var root = Path.Combine(Path.GetTempPath(), "fixture-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path.Combine(root, "home", "mnt", "d1"));
			return root;
		}

		private static Func<string, string?> Env(Dictionary<string, string> values)
			=> name => values.TryGetValue(name, out var v) ? v : null;

		private static Dictionary<string, string> Gated(string root, string fixture) => new()
		{
			["FILES_HEADLESS"] = "1",
			["FILES_HEADLESS_ROOT"] = root,
			["HOME"] = Path.Combine(root, "home"),
			["FILES_HEADLESS_DRIVES"] = fixture,
		};

		[TestMethod]
		public void LoadsValidEntriesAndDropsInvalidOnes()
		{
			var root = NewRoot();
			try
			{
				var file = Path.Combine(root, "home", "d.txt");
				File.WriteAllText(file, "# c\nAB12CD34|ext4|1000|400|fixed|d1\nbad line\nX|ext4|10|20|fixed|d1\nY|ext4|10|5|fixed|missing\nZ|ext4|10|5|fixed|../d1\n");
				var drives = HeadlessDriveFixture.LoadFromEnvironment(Env(Gated(root, file)));
				Assert.IsNotNull(drives);
				Assert.AreEqual(1, drives!.Count);
				Assert.AreEqual("AB12CD34", drives[0].Label);
				Assert.AreEqual(400UL, drives[0].FreeBytes);
				Assert.AreEqual(Path.Combine(root, "home", "mnt", "d1"), drives[0].MountPoint);
			}
			finally { Directory.Delete(root, true); }
		}

		[TestMethod]
		public void IgnoredWithoutTheHeadlessFlagOrOutsideTheSandboxHome()
		{
			var root = NewRoot();
			try
			{
				var file = Path.Combine(root, "home", "d.txt");
				File.WriteAllText(file, "A|ext4|10|5|fixed|d1\n");

				var noFlag = Gated(root, file);
				noFlag.Remove("FILES_HEADLESS");
				Assert.IsNull(HeadlessDriveFixture.LoadFromEnvironment(Env(noFlag)));

				var otherHome = Gated(root, file);
				otherHome["HOME"] = "/home/someone";
				Assert.IsNull(HeadlessDriveFixture.LoadFromEnvironment(Env(otherHome)));

				var outside = Gated(root, "/etc/passwd");
				Assert.IsNull(HeadlessDriveFixture.LoadFromEnvironment(Env(outside)));
			}
			finally { Directory.Delete(root, true); }
		}

		[TestMethod]
		public void SymlinkOversizeAndNonRegularFixturesYieldNoDrives()
		{
			var root = NewRoot();
			try
			{
				var real = Path.Combine(root, "home", "real.txt");
				File.WriteAllText(real, "A|ext4|10|5|fixed|d1\n");
				var link = Path.Combine(root, "home", "link.txt");
				File.CreateSymbolicLink(link, real);
				Assert.AreEqual(0, HeadlessDriveFixture.LoadFromEnvironment(Env(Gated(root, link)))!.Count);

				var big = Path.Combine(root, "home", "big.txt");
				File.WriteAllText(big, new string('#', HeadlessDriveFixture.MaxFileBytes + 1));
				Assert.AreEqual(0, HeadlessDriveFixture.LoadFromEnvironment(Env(Gated(root, big)))!.Count);

				var dir = Path.Combine(root, "home", "mnt");
				Assert.AreEqual(0, HeadlessDriveFixture.LoadFromEnvironment(Env(Gated(root, dir)))!.Count);
			}
			finally { Directory.Delete(root, true); }
		}
	}
}
