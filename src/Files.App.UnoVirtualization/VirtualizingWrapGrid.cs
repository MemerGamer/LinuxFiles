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
			return _layout.CorrectExtent(_layout.MeasureOverride(availableSize), availableSize);
		}

		public override Size ArrangeOverride(Size finalSize) => _layout.CorrectExtent(_layout.ArrangeOverride(finalSize), finalSize);

		/// <summary>
		/// Gets the number of items in a row (a column for the vertical-wrap orientation) at the current size.
		/// </summary>
		public int ItemsPerRow => _layout.ItemsPerRow;

		/// <summary>
		/// Gets the scroll direction: Vertical when items wrap horizontally into rows, Horizontal when they wrap vertically into columns.
		/// </summary>
		public Orientation PanelScrollOrientation => ScrollOrientation;

		/// <summary>
		/// Scrolls the minimum distance that shows the whole cell of the item at <paramref name="index"/>.
		/// </summary>
		public void EnsureItemVisible(int index) => _layout.EnsureItemVisible(index);

		private const string SupportedUnoVersionPrefix = "6.7.";
		private static string? s_unsupportedReason;
		private static bool s_probed;

		/// <summary>
		/// Checks that the Uno internals this panel builds on are present, so callers can fall back to a plain panel (and log
		/// <paramref name="reason"/>) after a Uno upgrade changes them. The result is computed once.
		/// </summary>
		public static bool IsSupported(out string? reason)
		{
			if (!s_probed)
			{
				try
				{
					var unoVersion = typeof(Panel).Assembly.GetName().Version?.ToString() ?? string.Empty;
					var informational = typeof(Panel).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
					var full = informational.Length > 0 ? ((System.Reflection.AssemblyInformationalVersionAttribute)informational[0]).InformationalVersion : unoVersion;
					if (!full.StartsWith(SupportedUnoVersionPrefix, StringComparison.Ordinal))
					{
						s_unsupportedReason = $"Uno.UI {full} is not the version the virtualizing wrap grid was built against ({SupportedUnoVersionPrefix}x)";
					}
					else
					{
						// Instantiating exercises the internal types and members the layout subclasses
						_ = ((IVirtualizingPanel)new VirtualizingWrapGrid()).GetLayouter();
					}
				}
				catch (Exception ex) when (ex is TypeLoadException or MissingMemberException or MethodAccessException or TypeInitializationException or BadImageFormatException or InvalidProgramException)
				{
					s_unsupportedReason = $"{ex.GetType().Name}: {ex.Message}";
				}

				s_probed = true;
			}

			reason = s_unsupportedReason;
			return s_unsupportedReason is null;
		}

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
			private bool _measurementQueued;
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

			public int ItemsPerRow => _itemsPerRow;

			/// <summary>
			/// Adopts the real cell size once a container has loaded (the item template sizes itself through bindings that only resolve after load)
			/// and re-derives the number of items per row. When either changed, the realized lines are rebuilt around the item that was
			/// at the top of the viewport, so a resize deep in a large folder does not realize items from the start.
			/// </summary>
			public void Update(double availableBreadth)
			{
				var anchorItem = GetAnchorItem();
				var changed = false;
				if (GetFirstMaterializedLine()?.FirstView is { IsLoaded: true } view)
				{
					// Measure the template's natural size, not the provisional virtual strip.
					view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
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
				if (++_consecutiveResizes > MaxConsecutiveResizes)
					return;

				if (anchorItem > 0 && ScrollViewer is not null)
					Reanchor(anchorItem);
				else
					Refresh();
			}

			private int GetAnchorItem()
			{
				if (GetFirstMaterializedLine() is null || ScrollViewer is null || _cellExtent <= 0)
					return -1;

				var count = ItemsControl?.NumberOfItems ?? 0;
				var row = (int)(ScrollOffset / _cellExtent);
				return Math.Clamp(row * _itemsPerRow, 0, Math.Max(0, count - 1));
			}

			private double? _pendingOffset;

			/// <summary>
			/// Rebuilds the realized lines around the current scroll offset (a plain refresh would realize items from the start up to the
			/// offset), then moves the scroll offset so the item that was at the top stays there once the new extent is known.
			/// </summary>
			private void Reanchor(int anchorItem)
			{
				_pendingOffset = anchorItem / _itemsPerRow * _cellExtent;

				var count = ItemsControl?.NumberOfItems ?? 0;
				var currentRow = Math.Min((int)(ScrollOffset / _cellExtent), Math.Max(0, (count - 1) / _itemsPerRow));
				ClearLines(clearContainer: true);
				Generator.ClearIdCache();
				SetDynamicSeed(Uno.UI.IndexPath.FromRowSection(currentRow * _itemsPerRow - 1, 0), currentRow * _cellExtent);
				UpdateLayout(null, isScroll: true);
				UpdateCompleted();

				ScheduleApplyPendingOffset(0);
			}

			private void ScheduleApplyPendingOffset(int attempt)
			{
				var dispatcher = OwnerPanel.DispatcherQueue;
				dispatcher?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => ApplyPendingOffset(attempt));
			}

			private void ApplyPendingOffset(int attempt)
			{
				if (_pendingOffset is not { } target || ScrollViewer is not { } scroller)
					return;

				var vertical = ScrollOrientation == Orientation.Vertical;
				var extent = vertical ? scroller.ExtentHeight : scroller.ExtentWidth;
				var viewport = vertical ? scroller.ViewportHeight : scroller.ViewportWidth;

				// The new extent arrives with the next layout pass; wait for it so the offset isn't clamped to the old one
				if (extent < target + viewport * 0.5 && attempt < 8)
				{
					ScheduleApplyPendingOffset(attempt + 1);
					return;
				}

				_pendingOffset = null;
				var clamped = Math.Clamp(target, 0, Math.Max(0, extent - viewport));
				scroller.ChangeView(vertical ? null : clamped, vertical ? clamped : null, null, disableAnimation: true);
			}

			/// <summary>
			/// Uno sizes the panel from per-item strips, which falls short of the real extent by the empty part of the last row.
			/// </summary>
			public Size CorrectExtent(Size size, Size available)
			{
				var count = ItemsControl?.NumberOfItems ?? 0;
				if (count == 0)
					return size;

				var extent = Math.Ceiling(count / (double)_itemsPerRow) * _cellExtent;
				var scrollsVertically = ScrollOrientation == Orientation.Vertical;
				var availableExtent = scrollsVertically ? available.Height : available.Width;
				extent = Math.Max(extent, double.IsInfinity(availableExtent) ? 0 : availableExtent);
				return scrollsVertically ? new Size(size.Width, extent) : new Size(extent, size.Height);
			}

			/// <summary>
			/// Scrolls the minimum distance that shows the whole cell of an item; Uno only knows the item's virtual strip.
			/// </summary>
			public void EnsureItemVisible(int index)
			{
				if (ScrollViewer is not { } scroller || index < 0)
					return;

				var start = index / _itemsPerRow * _cellExtent;
				var end = start + _cellExtent;
				var vertical = ScrollOrientation == Orientation.Vertical;
				var offset = vertical ? scroller.VerticalOffset : scroller.HorizontalOffset;
				var viewport = vertical ? scroller.ViewportHeight : scroller.ViewportWidth;

				double? target = null;
				if (start < offset)
					target = start;
				else if (end > offset + viewport)
					target = Math.Max(0, end - viewport);

				if (target is { } value)
					scroller.ChangeView(vertical ? null : value, vertical ? value : null, null, disableAnimation: true);
			}

			public override Line CreateLine(GeneratorDirection fillDirection, double extentOffset, double availableBreadth, Uno.UI.IndexPath nextVisibleItem)
			{
				var flat = GetFlatItemIndex(nextVisibleItem);
				var view = Generator.DequeueViewForItem(flat)!;
				AddView(view, fillDirection, extentOffset, 0);
				if (!_cellMeasured && !_measurementQueued)
				{
					_measurementQueued = true;
					OwnerPanel.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
					{
						_measurementQueued = false;
						OwnerPanel.InvalidateMeasure();
					});
				}

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
