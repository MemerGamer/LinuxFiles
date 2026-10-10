// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux;
using Files.Platform.Linux.Windowing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Windowing
{
	[TestClass]
	public sealed class CompositorScaleSettingsTests
	{
		[TestMethod]
		[DataRow("{}", false)]
		[DataRow("\uFEFF{\"DetectCompositorDisplayScale\":true}", true)]
		[DataRow("{\"DetectCompositorDisplayScale\":true}", true)]
		[DataRow("{\"DetectCompositorDisplayScale\":false}", false)]
		[DataRow("{\"DetectCompositorDisplayScale\":\"true\"}", false)]
		[DataRow("{\"DetectCompositorDisplayScale\":1}", false)]
		[DataRow("null", false)]
		[DataRow("[]", false)]
		[DataRow("{", false)]
		public void Read_OnlyAcceptsPersistedBooleanTrue(string json, bool expected)
		{
			var root = Path.Combine(Path.GetTempPath(), "files-scale-" + Guid.NewGuid().ToString("N"));
			var paths = new LinuxAppDataPaths(key => key == "XDG_CONFIG_HOME" ? root : null, root);
			try
			{
				Directory.CreateDirectory(paths.SettingsDirectory);
				Assert.IsFalse(CompositorScaleSettings.Read(paths.UserSettingsFilePath));
				File.WriteAllText(paths.UserSettingsFilePath, json);
				Assert.AreEqual(expected, CompositorScaleSettings.Read(paths.UserSettingsFilePath));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[TestMethod]
		public void Read_DirectoryDefaultsOff()
		{
			Assert.IsFalse(CompositorScaleSettings.Read(Path.GetTempPath()));
		}

		[TestMethod]
		public void Read_FifoDefaultsOffWithoutBlocking()
		{
			if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/mkfifo"))
				Assert.Inconclusive("Linux and mkfifo are required.");

			var path = Path.Combine(Path.GetTempPath(), "files-scale-fifo-" + Guid.NewGuid().ToString("N"));
			Task<bool>? read = null;
			try
			{
				var info = new ProcessStartInfo("/usr/bin/mkfifo") { UseShellExecute = false };
				info.ArgumentList.Add(path);
				using (var process = Process.Start(info)!)
				{
					try
					{
						Assert.IsTrue(process.WaitForExit(1000));
						Assert.AreEqual(0, process.ExitCode);
					}
					finally
					{
						if (!process.HasExited)
						{
							process.Kill(entireProcessTree: true);
							process.WaitForExit(500);
						}
					}
				}
				read = Task.Run(() => CompositorScaleSettings.Read(path));
				Assert.IsTrue(read.Wait(1000), "Reading a FIFO must not wait for a writer.");
				Assert.IsFalse(read.Result);
			}
			finally
			{
				if (read is not null && !read.IsCompleted)
				{
					// Unblock a regressed reader so the test doesn't leave a waiting thread.
					using var unblock = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
					read.Wait(1000);
				}
				File.Delete(path);
			}
		}


		[TestMethod]
		public void Read_OversizedFileDefaultsOff()
		{
			var path = Path.GetTempFileName();
			try
			{
				File.WriteAllText(path, new string(' ', 1024 * 1024 + 1));
				Assert.IsFalse(CompositorScaleSettings.Read(path));
			}
			finally
			{
				File.Delete(path);
			}
		}
	}
}
