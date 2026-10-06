// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Elevation;
using Files.Platform.Linux.Elevation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace Files.Platform.Tests.SystemIntegration
{
	[TestClass]
	public sealed class ElevationPlanPreviewTests
	{
		private static ElevatedPlanResult RenamePlan(string name)
			=> string.IsNullOrEmpty(name) || name.Contains('/')
				? ElevatedPlanResult.Refuse("bad name")
				: ElevatedPlanResult.Ok(new ElevatedPlan(ElevatedOperation.Rename, ["/opt/a"], name, [new ElevatedCommand("/usr/bin/mv", ["-n", "-T", "--", "/opt/a", "/opt/" + name])]));

		[TestMethod]
		public void ConfirmReturnsTheDisplayedPlanForTheTextOnScreen()
		{
			var preview = new ElevationPlanPreview(RenamePlan);
			preview.Update("one");
			preview.Update("two");

			Assert.IsTrue(preview.CanConfirm);
			StringAssert.Contains(preview.DisplayText, "/opt/two");
			var plan = preview.Confirm(preview.DisplayText);
			Assert.AreEqual("two", plan!.Target);
			Assert.AreEqual("/opt/two", plan.Commands[0].Arguments[^1]);
		}

		[TestMethod]
		public void StaleTextInvalidAndSecondConfirmsNeverReturnAPlan()
		{
			var preview = new ElevationPlanPreview(RenamePlan);
			preview.Update("one");
			var staleText = preview.DisplayText;
			preview.Update("two");
			Assert.IsNull(preview.Confirm(staleText), "the text on screen is not the displayed plan's text");

			preview.Update("a/b");
			Assert.IsFalse(preview.CanConfirm);
			Assert.IsNull(preview.Confirm(preview.DisplayText), "an invalid plan");

			preview.Update("three");
			Assert.IsNotNull(preview.Confirm(preview.DisplayText));
			Assert.IsNull(preview.Confirm(preview.DisplayText), "double confirm");
			preview.Update("four");
			Assert.IsFalse(preview.CanConfirm, "late updates after confirming are ignored");
			StringAssert.Contains(preview.DisplayText, "/opt/three");
		}

		[TestMethod]
		public void ClosedPreviewAndOversizedPlansNeverConfirm()
		{
			var preview = new ElevationPlanPreview(RenamePlan);
			preview.Update("one");
			preview.Close();
			Assert.IsNull(preview.Confirm(preview.DisplayText));
			Assert.IsFalse(preview.CanConfirm);

			var many = Enumerable.Range(0, 1500).Select(i => "/opt/f" + i).ToList();
			var huge = new ElevationPlanPreview(_ => ElevatedPlanResult.Ok(new ElevatedPlan(ElevatedOperation.Delete, many, null, [new ElevatedCommand("/usr/bin/rm", ["-rf", "--", .. many])])));
			huge.Update(string.Empty);
			Assert.IsFalse(huge.CanConfirm);
			Assert.IsNull(huge.Confirm(huge.DisplayText));
		}

		[TestMethod]
		public void PreviewFreezesMutableCollectionsBeforeConfirmation()
		{
			var sources = new List<string> { "/opt/a" };
			var arguments = new List<string> { "delete", "/opt/a" };
			var commands = new List<ElevatedCommand> { new(ElevationHelperProtocol.HelperPath, arguments) };
			var preview = new ElevationPlanPreview(_ => ElevatedPlanResult.Ok(new(ElevatedOperation.Delete, sources, null, commands)));
			preview.Update(string.Empty);
			var displayed = preview.DisplayText;
			sources[0] = "/etc/passwd"; arguments[1] = "/etc/passwd"; commands.Clear();
			var confirmed = preview.Confirm(displayed)!;
			Assert.AreEqual("/opt/a", confirmed.Sources.Single());
			Assert.AreEqual("/opt/a", confirmed.Commands.Single().Arguments[1]);
		}

		[TestMethod]
		public void DisplayEscapesTrickyNames()
		{
			var preview = new ElevationPlanPreview(RenamePlan);
			preview.Update("x‮y\nrm");
			Assert.IsFalse(preview.DisplayText.Contains('‮'));
			Assert.IsFalse(preview.DisplayText.Contains("y\nrm"));
		}
	}
}
