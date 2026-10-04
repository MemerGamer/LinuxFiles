// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.FileOperations
{
	/// <summary>
	/// Optional behavior of a file operation.
	/// </summary>
	public sealed record FileOperationOptions
	{
		/// <summary>Receives progress snapshots. Invoked from the thread running the operation.</summary>
		public IProgress<FileOperationProgress>? Progress { get; init; }

		/// <summary>
		/// Asked whenever a destination exists. When <see langword="null"/>, existing destinations are never touched and the item fails with
		/// <see cref="FileOperationErrorKind.AlreadyExists"/>.
		/// </summary>
		public Func<FileConflict, ValueTask<ConflictResolution>>? ConflictResolver { get; init; }

		/// <summary>Copies the content of symbolic links instead of recreating the links. Defaults to <see langword="false"/>.</summary>
		public bool FollowSymlinks { get; init; }
	}
}
