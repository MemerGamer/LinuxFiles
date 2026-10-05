// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Files.App.Views.Layouts
{
	/// <summary>
	/// Wraps file tiles between full-row headers (full-column headers for the List layout).
	/// </summary>
	internal sealed class LinuxGroupedWrapPanel : Panel
	{
		public Orientation Orientation { get; set; }

		protected override Size MeasureOverride(Size availableSize) => Layout(availableSize, false);
		protected override Size ArrangeOverride(Size finalSize)
		{
			Layout(finalSize, true);
			return finalSize;
		}

		private Size Layout(Size size, bool arrange)
		{
			var vertical = Orientation == Orientation.Vertical;
			var limit = vertical ? size.Height : size.Width;
			var breadth = 0d;
			var extent = 0d;
			var lineExtent = 0d;
			var maxBreadth = 0d;

			foreach (var child in Children)
			{
				var header = child is ContentControl { Content: IGroupedCollectionHeader };
				if (!arrange)
					child.Measure(header && !vertical ? new Size(size.Width, double.PositiveInfinity) : new Size(double.PositiveInfinity, double.PositiveInfinity));

				var childBreadth = vertical ? child.DesiredSize.Height : child.DesiredSize.Width;
				var childExtent = vertical ? child.DesiredSize.Width : child.DesiredSize.Height;
				if (header || (breadth > 0 && breadth + childBreadth > limit))
				{
					extent += lineExtent;
					breadth = 0;
					lineExtent = 0;
				}

				if (header && !vertical && double.IsFinite(limit))
					childBreadth = limit;

				if (arrange)
				{
					child.Arrange(vertical
						? new Rect(extent, breadth, childExtent, childBreadth)
						: new Rect(breadth, extent, childBreadth, childExtent));
				}

				breadth += childBreadth;
				maxBreadth = Math.Max(maxBreadth, breadth);
				lineExtent = Math.Max(lineExtent, childExtent);
				if (header && !vertical)
				{
					extent += lineExtent;
					breadth = 0;
					lineExtent = 0;
				}
			}

			extent += lineExtent;
			return vertical ? new Size(extent, maxBreadth) : new Size(maxBreadth, extent);
		}
	}
}
#endif
