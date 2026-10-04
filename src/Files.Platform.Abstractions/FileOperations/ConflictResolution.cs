// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Abstractions.FileOperations
{
	/// <summary>
	/// What to do when the destination of an item already exists.
	/// </summary>
	public enum ConflictAction
	{
		/// <summary>Leave the existing item and do not process the source.</summary>
		Skip = 0,

		/// <summary>Replace the existing file, or merge a folder into the existing folder.</summary>
		Overwrite,

		/// <summary>Keep both by giving the new item a unique name such as "name (2).ext".</summary>
		KeepBoth,

		/// <summary>Abort the whole operation.</summary>
		Cancel,
	}

	/// <summary>
	/// The answer of a conflict resolver.
	/// </summary>
	/// <param name="Action">The chosen action.</param>
	/// <param name="ApplyToAll">Whether to reuse <paramref name="Action"/> for all later conflicts of this operation.</param>
	public readonly record struct ConflictResolution(ConflictAction Action, bool ApplyToAll = false);
}
