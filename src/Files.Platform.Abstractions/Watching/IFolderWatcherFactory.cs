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
	/// Creates <see cref="IFolderWatcher"/> instances.
	/// </summary>
	public interface IFolderWatcherFactory
	{
		/// <summary>
		/// Creates a stopped watcher for the folder. The caller owns and disposes it.
		/// </summary>
		IFolderWatcher Create(string folderPath, FolderWatcherOptions? options = null);
	}
}
