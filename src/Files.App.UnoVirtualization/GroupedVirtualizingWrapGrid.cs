// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Specialized;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace Files.App.UnoVirtualization
{
	/// <summary>
	/// Virtualizes flattened grouped tiles using exact row geometry, including full-width headers.
	/// Uses the same publicized Uno 6.7 layout API as VirtualizingWrapGrid.
	/// </summary>
	public sealed class GroupedVirtualizingWrapGrid : Panel, IVirtualizingPanel
	{
		private readonly GroupedGridLayout layout;

		public GroupedVirtualizingWrapGrid()
		{
			layout = new GroupedGridLayout { CacheLength = 0d };
			layout.Initialize(this);
			Unloaded += (_, _) => layout.Detach();
		}

		public Orientation Orientation { get; set; } = Orientation.Horizontal;
		public Size ProvisionalCellSize { get; set; } = new(120, 140);
		public Func<object, bool> IsHeader { get; set; } = _ => false;
		public Orientation PanelScrollOrientation => Orientation == Orientation.Horizontal ? Orientation.Vertical : Orientation.Horizontal;
		public override Orientation? PhysicalOrientation => PanelScrollOrientation;
		public override bool WantsScrollViewerToObscureAvailableSizeBasedOnScrollBarVisibility(Orientation orientation) => PanelScrollOrientation == orientation;
		VirtualizingPanelLayout IVirtualizingPanel.GetLayouter() => layout;

		public override Size MeasureOverride(Size availableSize)
		{
			layout.Prepare(this, availableSize);
			return layout.CorrectExtent(layout.MeasureOverride(availableSize), availableSize);
		}

		public override Size ArrangeOverride(Size finalSize)
		{
			var result = layout.CorrectExtent(layout.ArrangeOverride(finalSize), finalSize);
			layout.Trace();
			return result;
		}
		public void EnsureItemVisible(int index) => layout.EnsureItemVisible(index);
		public int Navigate(int index, bool acrossLines, bool forward) => layout.Geometry.Navigate(index, acrossLines, forward);

		private sealed class GroupedGridLayout : VirtualizingPanelLayout
		{
			private const double MinReliableSize = 8;
			private double cellBreadth = 120;
			private double cellExtent = 140;
			private bool cellMeasured;
			private bool measurementQueued;
			private bool dirty = true;
			private int perRow = 1;
			private object? source;
			private INotifyCollectionChanged? notifications;
			private ScrollViewer? scroller;
			private int offsetRequest;
			private static readonly bool traceEnabled = Environment.GetEnvironmentVariable("FILES_GROUPED_LAYOUT_TRACE") == "1";
			private (int Count, int Realized, int First, double Offset, double Extent)? lastTrace;
			private readonly Dictionary<int, double> headerSizes = [];
			private readonly HashSet<int> measuredHeaders = [];
			private readonly Dictionary<int, FrameworkElement> headers = [];

			public GroupedWrapLayout Geometry { get; private set; } = new(0, 1, 140, _ => null);
			public override Orientation ScrollOrientation => Orientation;
			public override int GetItemsPerLine() => perRow;

			public void Trace()
			{
				if (!traceEnabled)
					return;
				var sample = (Count: Geometry.Count, Realized: _materializedLines.Sum(line => line.Items.Length), First: GetFirstMaterializedLine()?.FirstItemFlat ?? -1, Offset: ScrollOffset, Extent: Geometry.Extent);
				if (lastTrace == sample)
					return;
				lastTrace = sample;
				Console.WriteLine($"[grouped-layout] items={sample.Count} realized={sample.Realized} first={sample.First} offset={sample.Offset:F1} extent={sample.Extent:F1}");
			}

			public void Detach()
			{
				if (notifications is not null)
					notifications.CollectionChanged -= OnCollectionChanged;
				if (scroller is not null)
					scroller.ViewChanged -= OnExactScrollChanged;
				notifications = null;
				scroller = null;
				source = null;
				headers.Clear();
				dirty = true;
				offsetRequest++;
			}

			private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
			{
				dirty = true;
				OwnerPanel.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => OwnerPanel.InvalidateMeasure());
			}

			public void Prepare(GroupedVirtualizingWrapGrid panel, Size available)
			{
				Orientation = panel.PanelScrollOrientation;
				if (ItemsControl is null)
					return;
				if (!ReferenceEquals(source, ItemsControl?.ItemsSource))
				{
					if (notifications is not null)
						notifications.CollectionChanged -= OnCollectionChanged;
					source = ItemsControl?.ItemsSource;
					notifications = source as INotifyCollectionChanged;
					if (notifications is not null)
						notifications.CollectionChanged += OnCollectionChanged;
					dirty = true;
				}

				// Uno's large-scroll handler seeds by average item height, which cannot represent header rows.
				if (ScrollViewer is { } viewer && !ReferenceEquals(scroller, viewer))
				{
					if (scroller is not null)
						scroller.ViewChanged -= OnExactScrollChanged;
					scroller = viewer;
					viewer.ViewChanged -= OnScrollChanged;
					viewer.ViewChanged += OnExactScrollChanged;
				}

				var breadth = GetBreadth(available);
				var anchor = Geometry.FindRow(ScrollOffset);
				var withinRow = anchor is { } old ? ScrollOffset - old.Start : 0;
				var changed = dirty || Geometry.Count != (ItemsControl?.NumberOfItems ?? 0);
				if (!cellMeasured)
				{
					cellBreadth = Math.Max(MinReliableSize, GetBreadth(panel.ProvisionalCellSize));
					cellExtent = Math.Max(MinReliableSize, GetExtent(panel.ProvisionalCellSize));
				}

				foreach (var line in _materializedLines)
				{
					if (line.FirstItemFlat >= Geometry.Count || Geometry.GetRow(line.FirstItemFlat).IsHeader || !line.FirstView.IsLoaded)
						continue;

					var view = line.FirstView;
					view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
					var measuredBreadth = GetBreadth(view.DesiredSize);
					var measuredExtent = GetExtent(view.DesiredSize);
					if (measuredBreadth >= MinReliableSize && measuredExtent >= MinReliableSize)
					{
						changed |= Math.Abs(cellBreadth - measuredBreadth) > .5 || Math.Abs(cellExtent - measuredExtent) > .5;
						cellBreadth = measuredBreadth;
						cellExtent = measuredExtent;
						cellMeasured = true;
					}
					break;
				}

				var columns = double.IsFinite(breadth) ? Math.Max(1, (int)(breadth / cellBreadth)) : 1;
				changed |= columns != perRow;
				perRow = columns;

				// Headers already exist in the flattened source; measuring them never realizes file tiles.
				var sizes = new Dictionary<int, double>();
				measuredHeaders.Clear();
				if (dirty || Geometry.Count != (ItemsControl?.NumberOfItems ?? 0))
				{
					headers.Clear();
					if (ItemsControl is { } items)
					{
						for (var i = 0; i < items.NumberOfItems; i++)
							if (panel.IsHeader(items.Items[i]) && items.Items[i] is FrameworkElement header)
								headers.Add(i, header);
					}
				}
				foreach (var (i, header) in headers)
				{
					header.Measure(ScrollOrientation == Orientation.Vertical
						? new Size(breadth, double.PositiveInfinity)
						: new Size(double.PositiveInfinity, breadth));
					var extent = Math.Max(MinReliableSize, GetExtent(header.DesiredSize));
					sizes[i] = extent;
					if (header.IsLoaded)
						measuredHeaders.Add(i);
					changed |= !headerSizes.TryGetValue(i, out var previous) || Math.Abs(previous - extent) > .5;
				}

				if (!changed)
					return;

				dirty = false;
				headerSizes.Clear();
				foreach (var pair in sizes)
					headerSizes.Add(pair.Key, pair.Value);
				Geometry = new GroupedWrapLayout(ItemsControl?.NumberOfItems ?? 0, perRow, cellExtent,
					i => headerSizes.TryGetValue(i, out var height) ? height : null);

				// Rebuild at the viewport, including after a Reset or a resize far down a large folder.
				_availableSize = available;
				ViewportSize = ScrollViewer?.ViewportMeasureSize ?? default;
				_pendingCollectionChanges.Clear();
				_scrollAdjustmentForCollectionChanges = null;
				SeedViewport(ScrollOffset, clearContainer: true);
				if (anchor is { First: > 0 } a && a.First < Geometry.Count)
				{
					var target = Geometry.GetRow(a.First).Start + Math.Min(withinRow, Geometry.GetRow(a.First).Extent);
					var request = ++offsetRequest;
					ApplyAnchorOffset(target, request, 0);
				}
			}

			private void ApplyAnchorOffset(double target, int request, int attempt)
			{
				OwnerPanel.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
				{
					if (request != offsetRequest || ScrollViewer is not { } viewer)
						return;
					var extent = ScrollOrientation == Orientation.Vertical ? viewer.ExtentHeight : viewer.ExtentWidth;
					if (Math.Abs(extent - Geometry.Extent) > 1 && attempt < 8)
					{
						ApplyAnchorOffset(target, request, attempt + 1);
						return;
					}
					ChangeOffset(target);
				});
			}

			private void SeedViewport(double offset, bool clearContainer)
			{
				ClearLines(clearContainer);
				Generator.ClearIdCache();
				if (Geometry.FindRow(offset) is { } row)
					SetDynamicSeed(Uno.UI.IndexPath.FromRowSection(row.First - 1, 0), row.Start);
				UpdateLayout(null, isScroll: true);
				UpdateCompleted();
			}

			private void OnExactScrollChanged(object? sender, ScrollViewerViewChangedEventArgs e)
			{
				if (dirty)
				{
					OwnerPanel.InvalidateMeasure();
					return;
				}
				var large = Math.Abs(ScrollOffset - _lastScrollOffset) > ViewportExtent || GetFirstMaterializedLine() is null;
				if (large)
					SeedViewport(ScrollOffset, clearContainer: false);
				else
					UpdateLayout(null, isScroll: true);
				ArrangeElements(_availableSize, ViewportSize);
				UpdateCompleted();
				_lastScrollOffset = ScrollOffset;
				if (large)
					OwnerPanel.InvalidateMeasure();
				Trace();
			}

			private void ChangeOffset(double value)
			{
				if (ScrollViewer is not { } viewer)
					return;
				var vertical = ScrollOrientation == Orientation.Vertical;
				var viewport = vertical ? viewer.ViewportHeight : viewer.ViewportWidth;
				var offset = Math.Clamp(value, 0, Math.Max(0, Geometry.Extent - viewport));
				viewer.ChangeView(vertical ? null : offset, vertical ? offset : null, null, disableAnimation: true);
				OnExactScrollChanged(viewer, new ScrollViewerViewChangedEventArgs());
			}

			public void EnsureItemVisible(int index)
			{
				if (index < 0 || index >= Geometry.Count || ScrollViewer is not { } viewer)
					return;
				offsetRequest++;
				var row = Geometry.GetRow(index);
				var viewport = ScrollOrientation == Orientation.Vertical ? viewer.ViewportHeight : viewer.ViewportWidth;
				if (row.Start < ScrollOffset)
				{
					var start = row.First > 0 && Geometry.GetRow(row.First - 1) is { IsHeader: true } header ? header.Start : row.Start;
					ChangeOffset(start);
				}
				else if (row.Start + row.Extent > ScrollOffset + viewport)
					ChangeOffset(row.Start + row.Extent - viewport);
			}

			public Size CorrectExtent(Size size, Size available)
			{
				var extent = GetExtent(available);
				extent = Math.Max(Geometry.Extent, double.IsFinite(extent) ? extent : 0);
				return ScrollOrientation == Orientation.Vertical ? new Size(size.Width, extent) : new Size(extent, size.Height);
			}

			public override Line CreateLine(GeneratorDirection fillDirection, double extentOffset, double availableBreadth, Uno.UI.IndexPath nextVisibleItem)
			{
				var row = Geometry.GetRow(GetFlatItemIndex(nextVisibleItem));
				var views = new (FrameworkElement container, Uno.UI.IndexPath index)[row.Count];
				for (var column = 0; column < row.Count; column++)
				{
					var index = row.First + column;
					var view = Generator.DequeueViewForItem(index)!;
					AddView(view, fillDirection, row.Start, column * cellBreadth);
					SetBounds(view, Bounds(index, availableBreadth));
					views[column] = (view, Uno.UI.IndexPath.FromRowSection(index, 0));
				}
				if (!measurementQueued && (row.IsHeader ? !measuredHeaders.Contains(row.First) : !cellMeasured))
				{
					measurementQueued = true;
					OwnerPanel.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
					{
						measurementQueued = false;
						OwnerPanel.InvalidateMeasure();
					});
				}
				return new Line(row.First, views);
			}

			private Rect Bounds(int index, double availableBreadth)
			{
				var row = Geometry.GetRow(index);
				var breadth = row.IsHeader ? availableBreadth : cellBreadth;
				var column = row.IsHeader ? 0 : (index - row.First) * cellBreadth;
				return ScrollOrientation == Orientation.Vertical
					? new Rect(column, row.Start, breadth, row.Extent)
					: new Rect(row.Start, column, row.Extent, breadth);
			}

			public override Rect GetElementArrangeBounds(int elementIndex, Rect containerBounds, Size windowConstraint, Size finalSize) => Bounds(elementIndex, GetBreadth(finalSize));
		}
	}
}
