// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Abstractions.Trash
{
	/// <summary>
	/// Specifies what happens when a restore destination already exists.
	/// </summary>
	public enum TrashRestoreConflictBehavior
	{
		/// <summary>
		/// Leave the item in the trash and report a failure.
		/// </summary>
		Fail,

		/// <summary>
		/// Delete the existing destination and restore over it.
		/// </summary>
		Replace,

		/// <summary>
		/// Restore under a unique name next to the existing destination.
		/// </summary>
		KeepBoth,
	}
}
