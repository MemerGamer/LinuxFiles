// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Abstractions.FileOperations
{
	/// <summary>
	/// Describes why a file operation failed for an item.
	/// </summary>
	public enum FileOperationErrorKind
	{
		/// <summary>The cause could not be classified.</summary>
		Unknown = 0,

		/// <summary>The source or a required directory does not exist.</summary>
		NotFound,

		/// <summary>The caller lacks permission, or the file system is read-only.</summary>
		AccessDenied,

		/// <summary>The destination has no space left (or the quota is exhausted).</summary>
		NoSpace,

		/// <summary>The resulting name exceeds the file system limit.</summary>
		NameTooLong,

		/// <summary>The requested name is empty or contains forbidden characters.</summary>
		InvalidName,

		/// <summary>The item is busy.</summary>
		InUse,

		/// <summary>The destination exists and no conflict resolver consented to replace it.</summary>
		AlreadyExists,

		/// <summary>The destination is the source itself or lies inside it.</summary>
		InvalidDestination,

		/// <summary>A file cannot replace a folder, or the other way around.</summary>
		TypeMismatch,

		/// <summary>The copied data did not match the source, so the destination was discarded.</summary>
		VerificationFailed,
	}
}
