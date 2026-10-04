// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.FileOperations
{
	/// <summary>
	/// Copies, moves, deletes, renames and creates files and folders.
	/// </summary>
	/// <remarks>
	/// Cancellation and user-chosen <see cref="ConflictAction.Cancel"/> do not throw; unprocessed items are reported as
	/// <see cref="FileOperationStatus.Cancelled"/>. Existing items are never replaced without the conflict resolver's consent.
	/// </remarks>
	public interface IFileOperationsService
	{
		/// <summary>Copies items into a destination folder and returns one result per source, in order.</summary>
		Task<IReadOnlyList<FileOperationItemResult>> CopyAsync(
			IReadOnlyList<string> sources,
			string destinationDirectory,
			FileOperationOptions? options = null,
			CancellationToken cancellationToken = default);

		/// <summary>Moves items into a destination folder and returns one result per source, in order.</summary>
		Task<IReadOnlyList<FileOperationItemResult>> MoveAsync(
			IReadOnlyList<string> sources,
			string destinationDirectory,
			FileOperationOptions? options = null,
			CancellationToken cancellationToken = default);

		/// <summary>Permanently deletes items (symbolic links are removed, never followed) and returns one result per path.</summary>
		Task<IReadOnlyList<FileOperationItemResult>> DeleteAsync(
			IReadOnlyList<string> paths,
			FileOperationOptions? options = null,
			CancellationToken cancellationToken = default);

		/// <summary>Renames an item within its folder.</summary>
		Task<FileOperationItemResult> RenameAsync(
			string path,
			string newName,
			FileOperationOptions? options = null,
			CancellationToken cancellationToken = default);

		/// <summary>Creates an empty folder.</summary>
		Task<FileOperationItemResult> CreateFolderAsync(
			string parentDirectory,
			string name,
			FileCreationCollision collision = FileCreationCollision.GenerateUniqueName,
			CancellationToken cancellationToken = default);

		/// <summary>Creates an empty file.</summary>
		Task<FileOperationItemResult> CreateFileAsync(
			string parentDirectory,
			string name,
			FileCreationCollision collision = FileCreationCollision.GenerateUniqueName,
			CancellationToken cancellationToken = default);
	}
}
