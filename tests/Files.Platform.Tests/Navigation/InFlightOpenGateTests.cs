// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Platform.Tests.Navigation
{
	[TestClass]
	public sealed class InFlightOpenGateTests
	{
		[TestMethod]
		public void SecondOpenOfSameTarget_IsRejectedUntilTheFirstFinishes()
		{
			var gate = new InFlightOpenGate();

			using (var first = gate.TryEnter("tab1:/a.zip"))
			{
				Assert.IsNotNull(first);
				Assert.IsNull(gate.TryEnter("tab1:/a.zip"));
				Assert.IsNotNull(gate.TryEnter("tab2:/a.zip"));
				Assert.IsNotNull(gate.TryEnter("tab1:/b.zip"));
			}

			using var again = gate.TryEnter("tab1:/a.zip");
			Assert.IsNotNull(again);
		}

		[TestMethod]
		public void DisposingALeaseTwice_DoesNotReleaseANewerLease()
		{
			var gate = new InFlightOpenGate();
			var first = gate.TryEnter("k");
			first!.Dispose();
			using var second = gate.TryEnter("k");
			first.Dispose();

			Assert.IsNull(gate.TryEnter("k"));
		}
	}
}
