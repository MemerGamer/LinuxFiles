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
	/// Describes a created, deleted or changed entry.
	/// </summary>
	public class FolderChangeEventArgs : EventArgs
	{
		/// <summary>
		/// Gets the absolute path of the affected entry.
		/// </summary>
		public string FullPath { get; }

		/// <summary>
		/// Gets the file name of the affected entry.
		/// </summary>
		public string Name { get; }

		public FolderChangeEventArgs(string fullPath)
		{
			FullPath = fullPath;
			Name = Path.GetFileName(fullPath);
		}
	}

	/// <summary>
	/// Describes a renamed or moved entry.
	/// </summary>
	public sealed class FolderRenamedEventArgs : FolderChangeEventArgs
	{
		/// <summary>
		/// Gets the previous absolute path.
		/// </summary>
		public string OldFullPath { get; }

		/// <summary>
		/// Gets the previous file name.
		/// </summary>
		public string OldName { get; }

		public FolderRenamedEventArgs(string oldFullPath, string fullPath) : base(fullPath)
		{
			OldFullPath = oldFullPath;
			OldName = Path.GetFileName(oldFullPath);
		}
	}

	/// <summary>
	/// Describes a watcher failure.
	/// </summary>
	public sealed class FolderWatcherErrorEventArgs : EventArgs
	{
		/// <summary>
		/// Gets the underlying error.
		/// </summary>
		public Exception Exception { get; }

		/// <summary>
		/// Gets whether the watcher recovered by switching to polling.
		/// </summary>
		public bool FellBackToPolling { get; }

		public FolderWatcherErrorEventArgs(Exception exception, bool fellBackToPolling)
		{
			Exception = exception;
			FellBackToPolling = fellBackToPolling;
		}
	}
}
