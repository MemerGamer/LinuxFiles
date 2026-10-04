// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Abstractions.FileOperations
{
	/// <summary>
	/// The outcome of an operation on one item.
	/// </summary>
	public enum FileOperationStatus
	{
		/// <summary>The item was processed.</summary>
		Succeeded = 0,

		/// <summary>The item was intentionally left alone because the user chose to skip it.</summary>
		Skipped,

		/// <summary>The item failed; see <see cref="FileOperationItemResult.ErrorKind"/>.</summary>
		Failed,

		/// <summary>The operation was cancelled before or while the item was processed.</summary>
		Cancelled,
	}
}
