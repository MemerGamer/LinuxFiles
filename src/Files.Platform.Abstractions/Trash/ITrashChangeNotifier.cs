// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;

namespace Files.Platform.Abstractions.Trash
{
	/// <summary>
	/// Notifies about changes inside the trash folders; shape-compatible with <c>Files.Core.Storage.Contracts.ITrashWatcher</c>.
	/// </summary>
	public interface ITrashChangeNotifier : IDisposable
	{
		/// <summary>
		/// Occurs when an item was added to the trash.
		/// </summary>
		event EventHandler<FileSystemEventArgs>? ItemAdded;

		/// <summary>
		/// Occurs when an item was removed from the trash.
		/// </summary>
		event EventHandler<FileSystemEventArgs>? ItemDeleted;

		/// <summary>
		/// Occurs when an item in the trash changed.
		/// </summary>
		event EventHandler<FileSystemEventArgs>? ItemChanged;

		/// <summary>
		/// Occurs when an item in the trash was renamed.
		/// </summary>
		event EventHandler<FileSystemEventArgs>? ItemRenamed;

		/// <summary>
		/// Occurs when the watcher lost events and the trash view should be reloaded.
		/// </summary>
		event EventHandler<FileSystemEventArgs>? RefreshRequested;

		/// <summary>
		/// Starts watching the trash folders.
		/// </summary>
		void StartWatcher();

		/// <summary>
		/// Stops watching the trash folders.
		/// </summary>
		void StopWatcher();
	}
}
