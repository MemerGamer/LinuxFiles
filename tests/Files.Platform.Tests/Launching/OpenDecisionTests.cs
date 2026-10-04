// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Launching;
using Files.Platform.Linux.Mime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;
using System.IO;
using System.Text;

namespace Files.Platform.Tests.Launching
{
	[TestClass]
	public sealed class OpenDecisionTests
	{
		[TestMethod]
		[DataRow(true, ExecutableKind.Binary, OpenAction.RunBinaryWithConfirm)]
		[DataRow(false, ExecutableKind.Binary, OpenAction.Refuse)]
		[DataRow(true, ExecutableKind.Script, OpenAction.RunScriptWithConfirm)]
		[DataRow(false, ExecutableKind.Script, OpenAction.OpenDefault)]
		[DataRow(true, ExecutableKind.None, OpenAction.OpenDefault)]
		[DataRow(false, ExecutableKind.None, OpenAction.OpenDefault)]
		public void ContentDecidesNeverTheName(bool exec, ExecutableKind kind, OpenAction expected)
			=> Assert.AreEqual(expected, OpenDecision.Decide(DesktopState.None, false, exec, kind, "text/plain"));

		[TestMethod]
		[DataRow("application/x-executable")]
		[DataRow("application/vnd.appimage")]
		[DataRow("application/x-desktop")]
		public void ExecutableMimeWithoutMatchingContent_IsRefused(string mime)
			=> Assert.AreEqual(OpenAction.Refuse, OpenDecision.Decide(DesktopState.None, false, true, ExecutableKind.None, mime));

		[TestMethod]
		public void Desktop_TrustedOnlyInApplicationsDirectory_ExecBitIsNotTrust()
		{
			Assert.AreEqual(OpenAction.LaunchDesktopTrusted, OpenDecision.Decide(DesktopState.Valid, true, false, ExecutableKind.None));
			Assert.AreEqual(OpenAction.LaunchDesktopConfirm, OpenDecision.Decide(DesktopState.Valid, false, true, ExecutableKind.None));
			Assert.AreEqual(OpenAction.Refuse, OpenDecision.Decide(DesktopState.Invalid, true, true, ExecutableKind.None));
		}

		[TestMethod]
		public void Sniff_UsesMagic()
		{
			Assert.AreEqual(ExecutableKind.Binary, OpenDecision.Sniff([0x7F, (byte)'E', (byte)'L', (byte)'F', 2]));
			Assert.AreEqual(ExecutableKind.Script, OpenDecision.Sniff(Encoding.ASCII.GetBytes("#!/bin/sh\n")));
			Assert.AreEqual(ExecutableKind.None, OpenDecision.Sniff(Encoding.ASCII.GetBytes("hello")));
		}

		private static string[] Lines(string text) => text.Replace("\r", "").Split('\n');

		private static DesktopEntryParser.Entry? Strict(string text, out string? error)
			=> DesktopEntryParser.ParseStrict(Lines(text), "/tmp/x.desktop", "x.desktop", CultureInfo.InvariantCulture, out error);

		[TestMethod]
		public void StrictParse_AcceptsPlainEntry()
		{
			var entry = Strict("[Desktop Entry]\nType=Application\nName=X\nExec=/bin/true %f\n[Desktop Action a]\nExec=evil\n", out var error);
			Assert.IsNull(error);
			Assert.AreEqual("/bin/true %f", entry!.Application.Exec);
		}

		[TestMethod]
		[DataRow("[Desktop Entry]\nType=Application\nName=X\nExec=a\nExec=b\n")]
		[DataRow("[Desktop Entry]\nType=Application\nType=Application\nName=X\nExec=a\n")]
		[DataRow("[Desktop Entry]\nType=Application\nName=X\nExec=a\n[Desktop Entry]\nExec=b\n")]
		[DataRow("[Desktop Entry]\nType=Link\nName=X\nExec=a\n")]
		[DataRow("[Desktop Entry]\nType=Application\nName=X\n")]
		[DataRow("[Desktop Entry]\nType=Application\nName=X\nExec=a\\nb\n")]
		[DataRow("[Desktop Entry]\nType=Application\nName=X\nExec=a\0b\n")]
		public void StrictParse_RejectsAmbiguousEntries(string text)
		{
			Assert.IsNull(Strict(text, out var error));
			Assert.IsNotNull(error);
		}

		[TestMethod]
		public void FileIdentity_DetectsChange()
		{
			var path = Path.Combine(Path.GetTempPath(), "files-ident-" + System.Guid.NewGuid().ToString("N"));
			File.WriteAllText(path, "a");
			try
			{
				var id = FileIdentity.TryCapture(path)!.Value;
				Assert.IsTrue(id.StillMatches(path));

				File.WriteAllText(path, "abc");
				Assert.IsFalse(id.StillMatches(path));

				var link = path + ".lnk";
				File.CreateSymbolicLink(link, path);
				Assert.IsNull(FileIdentity.TryCapture(link));
				File.Delete(link);
			}
			finally
			{
				File.Delete(path);
			}
		}
	}
}
