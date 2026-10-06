// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using Files.App.UnoVirtualization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Platform.Tests.Listing
{
	[TestClass]
	public sealed class GroupedWrapLayoutTests
	{
		[TestMethod]
		public void PartialRowsAndConsecutiveHeadersHaveExactExtent()
		{
			var layout = new GroupedWrapLayout(10, 3, 100, i => i switch { 0 => 40, 3 => 60, 4 => 30, _ => null });
			Assert.AreEqual(430d, layout.Extent);
			Assert.AreEqual(new GroupedWrapLayout.Row(1, 2, 40, 100, false), layout.GetRow(2));
			Assert.AreEqual(new GroupedWrapLayout.Row(8, 2, 330, 100, false), layout.GetRow(9));
			Assert.AreEqual(3, layout.FindRow(140)!.Value.First);
			Assert.AreEqual(8, layout.FindRow(double.MaxValue)!.Value.First);
			Assert.AreEqual(0, layout.FindRow(-1)!.Value.First);
		}

		[TestMethod]
		public void NavigationSkipsHeadersAndClampsColumnInPartialRows()
		{
			var layout = new GroupedWrapLayout(10, 3, 100, i => i is 0 or 3 or 4 ? 40 : null);
			Assert.AreEqual(1, layout.Navigate(-1, true, true));
			Assert.AreEqual(5, layout.Navigate(2, false, true));
			Assert.AreEqual(6, layout.Navigate(2, true, true));
			Assert.AreEqual(9, layout.Navigate(7, true, true));
			Assert.AreEqual(2, layout.Navigate(7, true, false));
			Assert.AreEqual(-1, layout.Navigate(9, true, true));
		}

		[TestMethod]
		public void TenThousandTilesMapDeepViewportAndResizeWithoutWalkingContainers()
		{
			const int count = 10004;
			static double? Header(int i) => i % 2501 == 0 ? 44 : null;
			var wide = new GroupedWrapLayout(count, 11, 152, Header);
			Assert.AreEqual(4 * (44 + Math.Ceiling(2500d / 11) * 152), wide.Extent);
			var anchor = wide.FindRow(wide.Extent * .85)!.Value;
			var narrow = new GroupedWrapLayout(count, 7, 152, Header);
			var resized = narrow.GetRow(anchor.First);
			Assert.AreEqual(resized, narrow.FindRow(resized.Start)!.Value);
			Assert.IsTrue(resized.First > 8000);
			Assert.IsTrue(narrow.Extent > wide.Extent);
		}

		[TestMethod]
		public void EmptyAndHeaderOnlySourcesHaveNoSelectableTargets()
		{
			var empty = new GroupedWrapLayout(0, 1, 100, _ => null);
			Assert.AreEqual(0d, empty.Extent);
			Assert.IsNull(empty.FindRow(0));
			Assert.AreEqual(-1, empty.Navigate(-1, false, true));
			var headers = new GroupedWrapLayout(2, 1, 100, _ => 40);
			Assert.AreEqual(80d, headers.Extent);
			Assert.AreEqual(-1, headers.Navigate(-1, false, true));
		}
	}
}
