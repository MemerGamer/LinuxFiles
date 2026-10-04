// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Watching
{
	/// <summary>
	/// Watches a folder for changes. Events are raised on background threads.
	/// </summary>
	public interface IFolderWatcher : IDisposable
	{
		/// <summary>
		/// Raised when an entry was created.
		/// </summary>
		event EventHandler<FolderChangeEventArgs>? Created;

		/// <summary>
		/// Raised when an entry was deleted.
		/// </summary>
		event EventHandler<FolderChangeEventArgs>? Deleted;

		/// <summary>
		/// Raised when an entry was modified.
		/// </summary>
		event EventHandler<FolderChangeEventArgs>? Changed;

		/// <summary>
		/// Raised when an entry was renamed or moved within the watched folder.
		/// </summary>
		event EventHandler<FolderRenamedEventArgs>? Renamed;

		/// <summary>
		/// Raised when events were lost (overflow) and the folder must be enumerated again.
		/// </summary>
		event EventHandler? RescanRequired;

		/// <summary>
		/// Raised when the watcher failed or degraded, for example when inotify limits are exhausted.
		/// </summary>
		event EventHandler<FolderWatcherErrorEventArgs>? Error;

		/// <summary>
		/// Gets the watched folder.
		/// </summary>
		string FolderPath { get; }

		/// <summary>
		/// Gets whether the watcher currently polls instead of using native notifications.
		/// </summary>
		bool IsPolling { get; }

		/// <summary>
		/// Starts raising events. Falls back to polling when native watching cannot be set up.
		/// </summary>
		/// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
		void Start();

		/// <summary>
		/// Stops raising events. The watcher can be started again.
		/// </summary>
		void Stop();
	}
}
