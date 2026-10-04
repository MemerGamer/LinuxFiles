// Copyright (c) Files Community
// Licensed under the MIT License.

using System;

namespace Files.Platform.Linux.FileOperations
{
	/// <summary>
	/// Diagnostic seams that let tests observe, or interfere between, steps of an operation. Not used in production.
	/// </summary>
	public sealed class LinuxFileOperationsHooks
	{
		/// <summary>Invoked right after the temporary file of a copy was created, before any data is written.</summary>
		public Action<string>? TemporaryFileCreated { get; init; }

		/// <summary>Invoked right after a destination folder was created, before its content is copied.</summary>
		public Action<string>? DirectoryCreated { get; init; }

		/// <summary>Invoked after a source file or folder was classified and right before it is opened for copying.</summary>
		public Action<string>? BeforeOpenSource { get; init; }

		/// <summary>Invoked after an entry was classified and right before it is deleted or descended into.</summary>
		public Action<string>? BeforeDeleteEntry { get; init; }
	}
}
