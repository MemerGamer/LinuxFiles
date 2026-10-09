// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.Platform.Abstractions.Watching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Files.Platform.Linux.Watching
{
	/// <summary>
	/// Creates inotify-backed folder watchers.
	/// </summary>
	public sealed class LinuxFolderWatcherFactory : IFolderWatcherFactory
	{
		private readonly ILogger<LinuxFolderWatcherFactory> logger;

		public LinuxFolderWatcherFactory(ILogger<LinuxFolderWatcherFactory>? logger = null)
		{
			this.logger = logger ?? NullLogger<LinuxFolderWatcherFactory>.Instance;
		}

		/// <inheritdoc/>
		public IFolderWatcher Create(string folderPath, FolderWatcherOptions? options = null)
		{
			ArgumentException.ThrowIfNullOrEmpty(folderPath);
			return new LinuxFolderWatcher(Path.GetFullPath(folderPath), options ?? new(), logger);
		}
	}
}
