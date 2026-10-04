// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Abstractions.FileOperations
{
	/// <summary>
	/// What to do when a new file or folder would collide with an existing one.
	/// </summary>
	public enum FileCreationCollision
	{
		/// <summary>Create "name (2).ext" instead.</summary>
		GenerateUniqueName = 0,

		/// <summary>Fail with <see cref="FileOperationErrorKind.AlreadyExists"/>.</summary>
		FailIfExists,

		/// <summary>Succeed and leave the existing item untouched (never truncates).</summary>
		OpenIfExists,
	}
}
