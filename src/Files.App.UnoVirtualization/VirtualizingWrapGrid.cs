// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace Files.App.UnoVirtualization
{
	/// <summary>
	/// A virtualizing wrapping grid for <see cref="GridView"/> on Uno Skia, where <see cref="ItemsWrapGrid"/> is not implemented.
	/// Only the lines inside the viewport are realized, so large folders stay cheap.
	/// </summary>
	/// <remarks>
	/// Plugs a multi-item line layout into Uno's <c>VirtualizingPanelLayout</c> (the same machinery as <see cref="ItemsStackPanel"/>).
	/// That layout API is internal to Uno.UI, so the project publicizes Uno.UI (Krafs.Publicizer), which also lets the runtime skip access checks.
	/// </remarks>
	public sealed class VirtualizingWrapGrid : Panel, IVirtualizingPanel
	{
		private readonly WrapGridLayout _layout;

		public VirtualizingWrapGrid()
		{
			_layout = new WrapGridLayout { CacheLength = 4d };
			_layout.Initialize(this);
			UpdateLayoutOrientation();
		}

		public static DependencyProperty OrientationProperty { get; } = DependencyProperty.Register(
			nameof(Orientation),
			typeof(Orientation),
			typeof(VirtualizingWrapGrid),
			new PropertyMetadata(Orientation.Horizontal, new PropertyChangedCallback((d, _) => ((VirtualizingWrapGrid)d).UpdateLayoutOrientation())));

		/// <summary>
		/// Gets or sets the direction items are laid out in before wrapping (same meaning as WrapPanel.Orientation).
		/// </summary>
		public Orientation Orientation
		{
			get => (Orientation)GetValue(OrientationProperty);
			set => SetValue(OrientationProperty, value);
		}

		private Orientation ScrollOrientation =>
			Orientation == Orientation.Horizontal ? Orientation.Vertical : Orientation.Horizontal;

		public override Orientation? PhysicalOrientation => ScrollOrientation;

		public override bool WantsScrollViewerToObscureAvailableSizeBasedOnScrollBarVisibility(Orientation orientation) =>
			ScrollOrientation == orientation;

		VirtualizingPanelLayout IVirtualizingPanel.GetLayouter() => _layout;

		public override Size MeasureOverride(Size availableSize)
		{
			_layout.Update(Orientation == Orientation.Horizontal ? availableSize.Width : availableSize.Height);
			return _layout.MeasureOverride(availableSize);
		}

		public override Size ArrangeOverride(Size finalSize) => _layout.ArrangeOverride(finalSize);

		public static DependencyProperty ProvisionalCellSizeProperty { get; } = DependencyProperty.Register(
			nameof(ProvisionalCellSize),
			typeof(Size),
			typeof(VirtualizingWrapGrid),
			new PropertyMetadata(new Size(120, 140)));

		/// <summary>
		/// Gets or sets the cell size assumed until the first container has loaded and reports its real size.
		/// </summary>
		public Size ProvisionalCellSize
		{
			get => (Size)GetValue(ProvisionalCellSizeProperty);
			set
			{
				SetValue(ProvisionalCellSizeProperty, value);
				_layout.SetProvisionalCell(value);
			}
		}

		private void UpdateLayoutOrientation() => _layout.Orientation = ScrollOrientation;

		/// <summary>
		/// Uno's layout works in lines of one item (its scroll-offset to item mapping assumes that). Each item therefore gets a virtual strip
		/// (cell extent / items per row) along the scroll axis, which keeps every item inside the real band of its row, and
		/// <see cref="GetElementArrangeBounds"/> places the container at its real grid cell.
		/// </summary>
		private sealed class WrapGridLayout : VirtualizingPanelLayout
		{
			private const double MinReliableSize = 8d;
			private const int MaxConsecutiveResizes = 4;

			private double _cellBreadth = 120d;
			private double _cellExtent = 140d;
			private int _itemsPerRow = 1;
			private bool _cellMeasured;
			private int _consecutiveResizes;

			public override Orientation ScrollOrientation => Orientation;

			public override int GetItemsPerLine() => 1;

			private double StripExtent => _cellExtent / _itemsPerRow;

			public void SetProvisionalCell(Size size)
			{
				if (_cellMeasured || size.Width < MinReliableSize || size.Height < MinReliableSize)
					return;

				_cellBreadth = ScrollOrientation == Orientation.Vertical ? size.Width : size.Height;
				_cellExtent = ScrollOrientation == Orientation.Vertical ? size.Height : size.Width;
			}

			/// <summary>
			/// Adopts the real cell size once a container has loaded (the item template sizes itself through bindings that only resolve after load)
			/// and re-derives the number of items per row, rebuilding the realized lines when either changed.
			/// </summary>
			public void Update(double availableBreadth)
			{
				var changed = false;
				if (GetFirstMaterializedLine()?.FirstView is { IsLoaded: true } view)
				{
					var breadth = GetBreadth(view.DesiredSize);
					var extent = GetExtent(view.DesiredSize);
					if (breadth >= MinReliableSize && extent >= MinReliableSize &&
						(Math.Abs(breadth - _cellBreadth) > 0.5 || Math.Abs(extent - _cellExtent) > 0.5 || !_cellMeasured))
					{
						_cellBreadth = breadth;
						_cellExtent = extent;
						_cellMeasured = true;
						changed = true;
					}
				}

				if (!double.IsInfinity(availableBreadth) && !double.IsNaN(availableBreadth))
				{
					var perRow = Math.Max(1, (int)(availableBreadth / _cellBreadth));
					if (perRow != _itemsPerRow)
					{
						_itemsPerRow = perRow;
						changed = true;
					}
				}

				if (!changed)
				{
					_consecutiveResizes = 0;
					return;
				}

				// Guards against a cell size that keeps changing between passes
				if (++_consecutiveResizes <= MaxConsecutiveResizes)
					Refresh();
			}

			public override Line CreateLine(GeneratorDirection fillDirection, double extentOffset, double availableBreadth, Uno.UI.IndexPath nextVisibleItem)
			{
				var flat = GetFlatItemIndex(nextVisibleItem);
				var view = Generator.DequeueViewForItem(flat)!;
				AddView(view, fillDirection, extentOffset, 0);

				// Pin the virtual strip: a container whose template has not sized itself yet must not distort the line positions
				var strip = StripExtent;
				var start = fillDirection == GeneratorDirection.Forward ? extentOffset : extentOffset - strip;
				SetBounds(view, ScrollOrientation == Orientation.Vertical
					? new Rect(0, start, _cellBreadth, strip)
					: new Rect(start, 0, strip, _cellBreadth));

				return new Line(flat, (view, nextVisibleItem));
			}

			public override Rect GetElementArrangeBounds(int elementIndex, Rect containerBounds, Size windowConstraint, Size finalSize)
			{
				var row = elementIndex / _itemsPerRow;
				var column = elementIndex % _itemsPerRow;
				return ScrollOrientation == Orientation.Vertical
					? new Rect(column * _cellBreadth, row * _cellExtent, _cellBreadth, _cellExtent)
					: new Rect(row * _cellExtent, column * _cellBreadth, _cellExtent, _cellBreadth);
			}
		}
	}
}
