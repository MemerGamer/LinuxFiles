// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Trash;
using System;
using System.Collections.Generic;
using System.IO;

namespace Files.Platform.Linux.Trash
{
	/// <summary>
	/// Watches the <c>files</c> directories of the trash folders.
	/// </summary>
	internal sealed class TrashWatcher : ITrashChangeNotifier
	{
		private readonly Func<IEnumerable<string>> _getFilesDirectories;
		private readonly object _gate = new();
		private readonly List<FileSystemWatcher> _watchers = [];

		public TrashWatcher(Func<IEnumerable<string>> getFilesDirectories)
		{
			_getFilesDirectories = getFilesDirectories;
		}

		public event EventHandler<FileSystemEventArgs>? ItemAdded;

		public event EventHandler<FileSystemEventArgs>? ItemDeleted;

		public event EventHandler<FileSystemEventArgs>? ItemChanged;

		public event EventHandler<FileSystemEventArgs>? ItemRenamed;

		public event EventHandler<FileSystemEventArgs>? RefreshRequested;

		public void StartWatcher()
		{
			lock (_gate)
			{
				StopCore();

				foreach (var directory in _getFilesDirectories())
				{
					try
					{
						var watcher = new FileSystemWatcher(directory)
						{
							NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
						};

						watcher.Created += (s, e) => ItemAdded?.Invoke(this, e);
						watcher.Deleted += (s, e) => ItemDeleted?.Invoke(this, e);
						watcher.Changed += (s, e) => ItemChanged?.Invoke(this, e);
						watcher.Renamed += (s, e) => ItemRenamed?.Invoke(this, e);
						watcher.Error += (s, e) => RefreshRequested?.Invoke(this, new FileSystemEventArgs(WatcherChangeTypes.Changed, directory, null));
						watcher.EnableRaisingEvents = true;
						_watchers.Add(watcher);
					}
					catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
					{
					}
				}
			}
		}

		public void StopWatcher()
		{
			lock (_gate)
				StopCore();
		}

		public void Dispose() => StopWatcher();

		private void StopCore()
		{
			foreach (var watcher in _watchers)
				watcher.Dispose();

			_watchers.Clear();
		}
	}
}
