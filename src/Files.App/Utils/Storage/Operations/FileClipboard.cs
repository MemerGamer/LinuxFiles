// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.ApplicationModel.DataTransfer;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// In-app clipboard for cut/copy/paste of files and folders on Linux.
	/// </summary>
	/// <remarks>
	/// LINUX-TODO(clipboard): W-CLIP replaces this with the system-wide file clipboard
	/// (text/uri-list + x-special/gnome-copied-files). Keep the surface (Set/Clear/Items/Operation/Changed) as the seam.
	/// </remarks>
	public static class FileClipboard
	{
		private static readonly object _lock = new();

		private static IReadOnlyList<string> _items = [];

		private static DataPackageOperation _operation = DataPackageOperation.Copy;

		public static IReadOnlyList<string> Items
		{
			get { lock (_lock) return _items; }
		}

		public static DataPackageOperation Operation
		{
			get { lock (_lock) return _operation; }
		}

		public static bool HasItems => Items.Count > 0;

		public static event EventHandler? Changed;

		public static void Set(IEnumerable<string> paths, DataPackageOperation operation)
		{
			lock (_lock)
			{
				_items = [.. paths.Where(p => !string.IsNullOrEmpty(p)).Distinct()];
				_operation = operation;
			}

			Changed?.Invoke(null, EventArgs.Empty);
		}

		public static void Clear()
		{
			lock (_lock)
				_items = [];

			Changed?.Invoke(null, EventArgs.Empty);
		}
	}
}
