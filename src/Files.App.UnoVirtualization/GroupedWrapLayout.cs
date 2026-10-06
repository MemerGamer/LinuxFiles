// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Files.App.UnoVirtualization
{
	/// <summary>
	/// Geometry of a flattened stream: headers occupy a line, tiles share the following lines.
	/// Coordinates are independent of the panel's scroll orientation.
	/// </summary>
	internal sealed class GroupedWrapLayout
	{
		internal readonly record struct Row(int First, int Count, double Start, double Extent, bool IsHeader);

		private readonly List<Row> rows = [];
		private readonly int[] itemRows;

		public int Count => itemRows.Length;
		public double Extent { get; }

		public GroupedWrapLayout(int count, int perRow, double cellExtent, Func<int, double?> headerExtent)
		{
			itemRows = new int[count];
			var offset = 0d;
			var first = 0;
			while (first < count)
			{
				var header = headerExtent(first);
				var length = 1;
				if (header is null)
				{
					while (length < perRow && first + length < count && headerExtent(first + length) is null)
						length++;
				}

				var extent = header ?? cellExtent;
				for (var i = first; i < first + length; i++)
					itemRows[i] = rows.Count;
				rows.Add(new Row(first, length, offset, extent, header.HasValue));
				offset += extent;
				first += length;
			}
			Extent = offset;
		}

		public Row GetRow(int index) => rows[itemRows[index]];

		public Row? FindRow(double offset)
		{
			if (rows.Count == 0)
				return null;

			var low = 0;
			var high = rows.Count - 1;
			while (low < high)
			{
				var mid = (low + high) / 2;
				if (rows[mid].Start + rows[mid].Extent <= offset)
					low = mid + 1;
				else
					high = mid;
			}
			return rows[low];
		}

		public int Navigate(int index, bool acrossLines, bool forward)
		{
			var direction = forward ? 1 : -1;
			if (index < 0)
			{
				foreach (var row in rows)
					if (!row.IsHeader)
						return row.First;
				return -1;
			}
			if (index >= Count)
				return -1;

			if (!acrossLines)
			{
				for (var next = index + direction; next >= 0 && next < Count; next += direction)
					if (!GetRow(next).IsHeader)
						return next;
				return -1;
			}

			var origin = GetRow(index);
			var column = index - origin.First;
			for (var line = itemRows[index] + direction; line >= 0 && line < rows.Count; line += direction)
			{
				var row = rows[line];
				if (!row.IsHeader)
					return row.First + Math.Min(column, row.Count - 1);
			}
			return -1;
		}
	}
}
