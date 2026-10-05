// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Fonts;
using Files.Platform.Linux.Elevation;
using Files.Platform.Linux.Fonts;
using Files.Platform.Linux.Launching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Fonts
{
	[TestClass]
	public sealed class UserFontInstallServiceTests
	{
		private sealed class FakeLocator : IExecutableLocator
		{
			public string? Locate(string command) => "/fake/" + command;
		}

		private sealed class RecordingRunner : IElevatedProcessRunner
		{
			public List<(string File, string[] Args)> Runs { get; } = new();

			public Task<(int ExitCode, string StandardError)> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
			{
				Runs.Add((fileName, [.. arguments]));
				return Task.FromResult((0, ""));
			}
		}

		private static string NewDirectory()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ffont-" + Guid.NewGuid().ToString("N")[..8]);
			Directory.CreateDirectory(dir);
			return dir;
		}

		[TestMethod]
		public async Task InstallsRealFontsAndRefreshesTheCacheWithoutAShell()
		{
			var root = NewDirectory();
			try
			{
				var source = Path.Combine(root, "My Font.ttf");
				File.WriteAllBytes(source, [0, 1, 0, 0, 9, 9]);
				var runner = new RecordingRunner();
				var fonts = Path.Combine(root, "fonts");
				var service = new UserFontInstallService(fonts, new FakeLocator(), runner);

				Assert.IsFalse(service.IsInstalled(source));
				Assert.AreEqual(FontInstallResult.Installed, await service.InstallAsync(source, false));
				Assert.IsTrue(File.Exists(Path.Combine(fonts, "My Font.ttf")));
				Assert.IsTrue(service.IsInstalled(source));
				Assert.AreEqual(1, runner.Runs.Count);
				Assert.AreEqual("/fake/fc-cache", runner.Runs[0].File);
				CollectionAssert.AreEqual(new[] { "-f", fonts }, runner.Runs[0].Args);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[TestMethod]
		public async Task RefusesNonFontsAndOverwriteWithoutPermission()
		{
			var root = NewDirectory();
			try
			{
				var fake = Path.Combine(root, "evil.ttf");
				File.WriteAllText(fake, "#!/bin/sh\necho hi");
				var good = Path.Combine(root, "good.otf");
				File.WriteAllBytes(good, "OTTOxxxx"u8.ToArray());
				var fonts = Path.Combine(root, "fonts");
				var service = new UserFontInstallService(fonts, new FakeLocator(), new RecordingRunner());

				Assert.AreEqual(FontInstallResult.NotAFont, await service.InstallAsync(fake, true));
				Assert.IsFalse(File.Exists(Path.Combine(fonts, "evil.ttf")));

				Assert.AreEqual(FontInstallResult.Installed, await service.InstallAsync(good, false));
				File.WriteAllBytes(good, "OTTOnew!"u8.ToArray());
				Assert.AreEqual(FontInstallResult.AlreadyExists, await service.InstallAsync(good, false));
				CollectionAssert.AreEqual("OTTOxxxx"u8.ToArray(), File.ReadAllBytes(Path.Combine(fonts, "good.otf")));
				Assert.AreEqual(FontInstallResult.Installed, await service.InstallAsync(good, true));
				CollectionAssert.AreEqual("OTTOnew!"u8.ToArray(), File.ReadAllBytes(Path.Combine(fonts, "good.otf")));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[TestMethod]
		public void RecognizesFontSignatures()
		{
			Assert.IsTrue(UserFontInstallService.IsFontSignature("wOF2"u8));
			Assert.IsTrue(UserFontInstallService.IsFontSignature("ttcf"u8));
			Assert.IsFalse(UserFontInstallService.IsFontSignature("MZ\0\0"u8));
			Assert.IsFalse(UserFontInstallService.IsFontSignature([0, 1]));
		}

		[TestMethod]
		public void UsesXdgDataHomeWhenAbsolute()
		{
			Assert.AreEqual("/x/y/fonts", UserFontInstallService.DefaultFontsDirectory(k => k == "XDG_DATA_HOME" ? "/x/y" : null));
			Assert.AreEqual("/home/u/.local/share/fonts", UserFontInstallService.DefaultFontsDirectory(k => k == "HOME" ? "/home/u" : "rel"));
		}
	}
}
