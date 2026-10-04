// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Clipboard
{
	/// <summary>
	/// The result of dragging files out of the application window.
	/// </summary>
	public enum FileDragOutcome
	{
		/// <summary>The drag ended without a drop on another application (cancelled, dropped on nothing, or unsupported).</summary>
		None,

		/// <summary>The receiving application accepted the files as a copy.</summary>
		Copy,

		/// <summary>The receiving application accepted the files as a move.</summary>
		Move,
	}

	/// <summary>
	/// Offers files to other applications while the user drags them over windows outside this application.
	/// </summary>
	public interface IFileDragSource
	{
		/// <summary>
		/// Gets whether dragging files to other applications is supported in this session.
		/// </summary>
		bool IsDragSupported { get; }

		/// <summary>
		/// Tracks the pointer while the primary button is held and drops <paramref name="paths"/> on the application under it, if any.
		/// Windows of this process are ignored, because in-app drags are handled by the UI framework.
		/// </summary>
		Task<FileDragOutcome> DragFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
	}
}
