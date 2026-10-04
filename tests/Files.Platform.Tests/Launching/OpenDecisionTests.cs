// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Launching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Platform.Tests.Launching
{
	[TestClass]
	public sealed class OpenDecisionTests
	{
		[TestMethod]
		[DataRow(true, ExecutableKind.Binary, OpenAction.RunBinaryWithConfirm)]
		[DataRow(true, ExecutableKind.Script, OpenAction.RunScriptWithConfirm)]
		[DataRow(true, ExecutableKind.None, OpenAction.OpenDefault)]
		[DataRow(false, ExecutableKind.Binary, OpenAction.OpenDefault)]
		[DataRow(false, ExecutableKind.Script, OpenAction.OpenDefault)]
		public void NonDesktop_NeverRunsWithoutConfirmation(bool exec, ExecutableKind kind, OpenAction expected)
			=> Assert.AreEqual(expected, OpenDecision.Decide(false, false, exec, kind));

		[TestMethod]
		public void Desktop_TrustedOnlyInApplicationsDirectory_ExecBitIsNotTrust()
		{
			Assert.AreEqual(OpenAction.LaunchDesktopTrusted, OpenDecision.Decide(true, true, false, ExecutableKind.None));
			Assert.AreEqual(OpenAction.LaunchDesktopConfirm, OpenDecision.Decide(true, false, true, ExecutableKind.None));
			Assert.AreEqual(OpenAction.LaunchDesktopConfirm, OpenDecision.Decide(true, false, false, ExecutableKind.None));
		}
	}
}
