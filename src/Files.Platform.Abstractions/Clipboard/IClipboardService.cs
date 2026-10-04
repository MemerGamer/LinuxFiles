// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Clipboard
{
	/// <summary>
	/// Exchanges file lists with other applications through the system clipboard, in the formats file managers use.
	/// </summary>
	public interface IClipboardService
	{
		/// <summary>
		/// Gets whether the clipboard backend is usable (for example an X server is reachable).
		/// </summary>
		bool IsAvailable { get; }

		/// <summary>
		/// Raised when the clipboard owner changes, including when another application takes the clipboard over.
		/// </summary>
		event EventHandler? ContentChanged;

		/// <summary>
		/// Takes ownership of the clipboard and offers <paramref name="paths"/> to other applications.
		/// </summary>
		/// <returns><see langword="false"/> when the clipboard could not be claimed.</returns>
		Task<bool> SetFilesAsync(IReadOnlyList<string> paths, ClipboardOperation operation, CancellationToken cancellationToken = default);

		/// <summary>
		/// Reads a file list from the clipboard, or <see langword="null"/> when it holds none.
		/// </summary>
		Task<ClipboardFileList?> GetFilesAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// Releases the clipboard if this application owns it (for example after a cut was pasted).
		/// </summary>
		Task ClearAsync(CancellationToken cancellationToken = default);
	}
}
