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
	/// Options controlling an <see cref="IFolderWatcher"/>.
	/// </summary>
	public sealed record FolderWatcherOptions
	{
		/// <summary>
		/// Gets whether sub-folders are watched too.
		/// </summary>
		public bool IncludeSubdirectories { get; init; }

		/// <summary>
		/// Gets whether changes to hidden (dot-prefixed) entries are reported.
		/// </summary>
		public bool IncludeHidden { get; init; } = true;

		/// <summary>
		/// Gets the coalescing window. Events are buffered for this long and repeated Changed events for
		/// the same path are merged. <see cref="TimeSpan.Zero"/> raises events immediately.
		/// </summary>
		public TimeSpan Debounce { get; init; } = TimeSpan.Zero;

		/// <summary>
		/// Gets the interval used when native watching is unavailable and polling is used instead.
		/// </summary>
		public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

		/// <summary>
		/// Gets whether polling is used even if native notifications are available (for example on network or FUSE mounts).
		/// </summary>
		public bool ForcePolling { get; init; }
	}
}
