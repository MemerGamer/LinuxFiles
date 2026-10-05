// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;

namespace Files.Platform.Linux.FileOperations
{
	/// <summary>
	/// Diagnostic seams that let tests observe, or interfere between, steps of an operation. Not used in production.
	/// </summary>
	public sealed class LinuxFileOperationsHooks
	{
		/// <summary>Returns an errno to simulate a rename failure, or null to invoke libc. Names are fd-relative.</summary>
		public Func<string, string, bool, int?>? RenameError { get; init; }

		internal ConcurrentDictionary<(uint Major, uint Minor), byte> UnsupportedRenameDevices { get; } = new();

		/// <summary>Invoked right after the temporary file of a copy was created, before any data is written.</summary>
		public Action<string>? TemporaryFileCreated { get; init; }

		/// <summary>Invoked right after a destination folder was created, before its content is copied.</summary>
		public Action<string>? DirectoryCreated { get; init; }

		/// <summary>Invoked after a source file or folder was classified and right before it is opened for copying.</summary>
		public Action<string>? BeforeOpenSource { get; init; }

		/// <summary>Invoked after both parents are opened and conflicts resolved, before a move acts.</summary>
		public Action<string, string>? BeforeMoveEntry { get; init; }

		/// <summary>Invoked after an entry was classified and right before it is deleted or descended into.</summary>
		public Action<string>? BeforeDeleteEntry { get; init; }
	}
}
