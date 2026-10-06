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
	/// Virtualizes flattened grouped tiles with full-width headers and cached estimates for unrealized headers.
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
			layout.CaptureAnchor();
			layout.Trace();
			return result;
		}
		public void EnsureItemVisible(object item, Action fallback) => layout.EnsureItemVisible(item, fallback);
		public int Navigate(int index, bool acrossLines, bool forward) => layout.Navigate(index, acrossLines, forward);

		private sealed class GroupedGridLayout : VirtualizingPanelLayout
		{
			private const double MinReliableSize = 8;
			private double cellBreadth = 120;
			private double cellExtent = 140;
			private bool cellMeasured;
			private bool measurementQueued;
			private bool dirty = true;
			private bool geometryNeedsRefresh = true;
			private int perRow = 1;
			private object? source;
			private INotifyCollectionChanged? notifications;
			private ScrollViewer? scroller;
			private int offsetRequest;
			private bool restoringAnchor;
			private double geometryBreadth;
			private GroupedLayoutAnchor? anchor;
			private (object Item, Action Fallback)? pendingScroll;
			private readonly Dictionary<(Type Kind, object? Template, double Breadth, Orientation Orientation), double> headerEstimates = [];
			private double? lastMeasuredHeaderExtent;
			private static readonly bool traceEnabled = Environment.GetEnvironmentVariable("FILES_GROUPED_LAYOUT_TRACE") == "1";
			private (int Count, int Realized, int First, double Offset, double Extent)? lastTrace;
			private readonly Dictionary<int, double> headerSizes = [];
			private readonly HashSet<int> measuredHeaders = [];
			private readonly Dictionary<int, double> loadedHeaderSizes = [];
			private readonly Dictionary<int, FrameworkElement> headers = [];

			public GroupedWrapLayout Geometry { get; private set; } = new(0, 1, 140, _ => null);
			public override Orientation ScrollOrientation => Orientation;
			public override int GetItemsPerLine() => perRow;
			private bool IsGeometryCurrent => !dirty && ReferenceEquals(source, ItemsControl?.ItemsSource) && Geometry.Count == (ItemsControl?.NumberOfItems ?? 0);

			public int Navigate(int index, bool acrossLines, bool forward)
			{
				var target = Geometry.Navigate(index, acrossLines, forward, ItemsControl?.NumberOfItems ?? 0, !IsGeometryCurrent);
				return target >= 0 && target < (ItemsControl?.NumberOfItems ?? 0) ? target : -1;
			}

			public void CaptureAnchor()
			{
				if (restoringAnchor || !IsGeometryCurrent || ItemsControl is not { } items || Geometry.FindRow(ScrollOffset) is not { } row)
					return;
				var index = row.IsHeader ? Geometry.Navigate(row.First, false, true) : row.First;
				if (index >= 0 && index < items.NumberOfItems)
					anchor = new GroupedLayoutAnchor(items.Items[index], ScrollOffset - Geometry.GetRow(index).Start);
			}

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
				anchor = null;
				restoringAnchor = false;
				pendingScroll = null;
				dirty = true;
				geometryNeedsRefresh = true;
				offsetRequest++;
			}

			private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
			{
				dirty = true;
				geometryNeedsRefresh = true;
				restoringAnchor = false;
				offsetRequest++;
				OwnerPanel.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => OwnerPanel.InvalidateMeasure());
			}

			public void Prepare(GroupedVirtualizingWrapGrid panel, Size available)
			{
				Orientation = panel.PanelScrollOrientation;
				if (ItemsControl is not { } itemsControl)
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
					geometryNeedsRefresh = true;
					restoringAnchor = false;
					offsetRequest++;
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
				var viewportAnchor = anchor;
				var changed = dirty || Geometry.Count != (ItemsControl?.NumberOfItems ?? 0) || breadth != geometryBreadth;
				if (!cellMeasured)
				{
					cellBreadth = Math.Max(MinReliableSize, GetBreadth(panel.ProvisionalCellSize));
					cellExtent = Math.Max(MinReliableSize, GetExtent(panel.ProvisionalCellSize));
				}

				foreach (var line in _materializedLines)
				{
					if (!IsGeometryCurrent || !Geometry.TryGetRow(line.FirstItemFlat, out var row) || row.IsHeader || !line.FirstView.IsLoaded)
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

				if (dirty || Geometry.Count != itemsControl.NumberOfItems)
					CollectHeaders(panel);

				// Only loaded headers have resolved templates and inherited resources.
				measuredHeaders.Clear();
				loadedHeaderSizes.Clear();
				foreach (var line in _materializedLines)
				{
					if (!headers.TryGetValue(line.FirstItemFlat, out var header) || !header.IsLoaded || !ReferenceEquals(header, line.FirstView))
						continue;
					header.Measure(ScrollOrientation == Orientation.Vertical
						? new Size(breadth, double.PositiveInfinity)
						: new Size(double.PositiveInfinity, breadth));
					var extent = GetExtent(header.DesiredSize);
					if (!double.IsFinite(extent) || extent < MinReliableSize)
						continue;
					measuredHeaders.Add(line.FirstItemFlat);
					loadedHeaderSizes[line.FirstItemFlat] = extent;
					var key = HeaderKey(header, breadth);
					changed |= !headerSizes.TryGetValue(line.FirstItemFlat, out var previous) || Math.Abs(previous - extent) > .5;
					headerEstimates[key] = extent;
					lastMeasuredHeaderExtent = extent;
				}

				QueuePendingScroll();
				if (!changed)
					return;

				RebuildGeometry(breadth);
				dirty = false;

				// Rebuild at the viewport, including after a Reset or a resize far down a large folder.
				_availableSize = available;
				ViewportSize = ScrollViewer?.ViewportMeasureSize ?? default;
				_pendingCollectionChanges.Clear();
				_scrollAdjustmentForCollectionChanges = null;
				SeedViewport(ScrollOffset, clearContainer: true);
				if (pendingScroll is null && viewportAnchor?.GetOffset(Geometry, item => itemsControl.Items.IndexOf(item)) is { } target)
				{
					var request = ++offsetRequest;
					restoringAnchor = true;
					ApplyAnchorOffset(target, request, 0);
				}
			}

			private void CollectHeaders(GroupedVirtualizingWrapGrid panel)
			{
				geometryNeedsRefresh = false;
				headers.Clear();
				measuredHeaders.Clear();
				loadedHeaderSizes.Clear();
				if (ItemsControl is not { } items)
					return;
				for (var i = 0; i < items.NumberOfItems; i++)
					if (panel.IsHeader(items.Items[i]) && items.Items[i] is FrameworkElement header)
						headers.Add(i, header);
			}

			private (Type Kind, object? Template, double Breadth, Orientation Orientation) HeaderKey(FrameworkElement header, double breadth) =>
				(header.GetType(), (header as ContentControl)?.ContentTemplate, breadth, ScrollOrientation);

			private void RebuildGeometry(double breadth)
			{
				geometryBreadth = breadth;
				headerSizes.Clear();
				foreach (var (i, header) in headers)
					headerSizes[i] = loadedHeaderSizes.TryGetValue(i, out var loadedExtent)
						? loadedExtent : headerEstimates.TryGetValue(HeaderKey(header, breadth), out var extent)
						? extent : lastMeasuredHeaderExtent ?? (ScrollOrientation == Orientation.Vertical ? 44 : cellExtent);
				Geometry = new GroupedWrapLayout(ItemsControl?.NumberOfItems ?? 0, perRow, cellExtent,
					i => headerSizes.TryGetValue(i, out var height) ? height : null);
			}

			private void ApplyAnchorOffset(double target, int request, int attempt)
			{
				OwnerPanel.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
				{
					if (request != offsetRequest || !IsGeometryCurrent || ScrollViewer is not { } viewer)
					{
						if (request == offsetRequest && ScrollViewer is null)
							restoringAnchor = false;
						return;
					}
					var extent = ScrollOrientation == Orientation.Vertical ? viewer.ExtentHeight : viewer.ExtentWidth;
					if (Math.Abs(extent - Geometry.Extent) > 1 && attempt < 8)
					{
						ApplyAnchorOffset(target, request, attempt + 1);
						return;
					}
					restoringAnchor = false;
					ChangeOffset(target);
					CaptureAnchor();
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
				if (!IsGeometryCurrent)
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
				CaptureAnchor();
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

			public void EnsureItemVisible(object item, Action fallback)
			{
				offsetRequest++;
				restoringAnchor = false;
				pendingScroll = null;
				if (IsGeometryCurrent && TryEnsureItemVisible(item))
					return;
				pendingScroll = (item, fallback);
				OwnerPanel.InvalidateMeasure();
			}

			private void QueuePendingScroll()
			{
				if (pendingScroll is not { } pending)
					return;
				var request = offsetRequest;
				OwnerPanel.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
				{
					if (request != offsetRequest || pendingScroll is null)
						return;
					if (!IsGeometryCurrent)
					{
						OwnerPanel.InvalidateMeasure();
						return;
					}
					pendingScroll = null;
					if (!TryEnsureItemVisible(pending.Item))
						pending.Fallback();
				});
			}

			private bool TryEnsureItemVisible(object item)
			{
				var index = ItemsControl?.Items.IndexOf(item) ?? -1;
				if (!Geometry.TryGetRow(index, out var row) || ScrollViewer is not { } viewer)
					return false;
				var viewport = ScrollOrientation == Orientation.Vertical ? viewer.ViewportHeight : viewer.ViewportWidth;
				if (viewport <= 0)
					return false;
				if (row.Start < ScrollOffset)
				{
					var start = row.First > 0 && Geometry.GetRow(row.First - 1) is { IsHeader: true } header ? header.Start : row.Start;
					ChangeOffset(start);
				}
				else if (row.Start + row.Extent > ScrollOffset + viewport)
					ChangeOffset(row.Start + row.Extent - viewport);
				return true;
			}

			public Size CorrectExtent(Size size, Size available)
			{
				var extent = GetExtent(available);
				extent = Math.Max(Geometry.Extent, double.IsFinite(extent) ? extent : 0);
				return ScrollOrientation == Orientation.Vertical ? new Size(size.Width, extent) : new Size(extent, size.Height);
			}

			public override Line CreateLine(GeneratorDirection fillDirection, double extentOffset, double availableBreadth, Uno.UI.IndexPath nextVisibleItem)
			{
				var flat = GetFlatItemIndex(nextVisibleItem);
				if (geometryNeedsRefresh || Geometry.Count != (ItemsControl?.NumberOfItems ?? 0))
				{
					CollectHeaders((GroupedVirtualizingWrapGrid)OwnerPanel);
					RebuildGeometry(availableBreadth);
					OwnerPanel.InvalidateMeasure();
				}
				if (!Geometry.TryGetRow(flat, out var row))
				{
					// Uno may still supply a retired seed during collection-change processing.
					var items = ItemsControl!;
					var placeholder = (FrameworkElement)items.GetContainerForTemplate(items.ItemTemplate);
					AddView(placeholder, fillDirection, extentOffset, 0);
					SetBounds(placeholder, ScrollOrientation == Orientation.Vertical
						? new Rect(0, extentOffset, availableBreadth, cellExtent)
						: new Rect(extentOffset, 0, cellExtent, availableBreadth));
					OwnerPanel.InvalidateMeasure();
					var clamped = Math.Clamp(flat, 0, Math.Max(0, Geometry.Count - 1));
					return new Line(clamped, (placeholder, Uno.UI.IndexPath.FromRowSection(clamped, 0)));
				}
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
				if (!Geometry.TryGetRow(index, out var row))
					return default;
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
