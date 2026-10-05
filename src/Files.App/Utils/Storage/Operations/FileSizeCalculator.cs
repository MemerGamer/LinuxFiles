// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Concurrent;

namespace Files.App.Utils.Storage.Operations
{
	internal sealed partial class FileSizeCalculator
	{
		private readonly string[] _paths;
		private readonly ConcurrentDictionary<string, long> _computedFiles = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		private long _size;

		public long Size => _size;
		public int ItemsCount => _computedFiles.Count;
		public bool Completed { get; private set; }

		public event Action<int>? ItemsCountChanged;

		public FileSizeCalculator(params string[] paths)
		{
			_paths = paths;
		}

		public bool TryGetComputedFileSize(string path, out long size)
		{
			return _computedFiles.TryGetValue(path, out size);
		}
	}
}
