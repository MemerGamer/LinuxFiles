// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Abstractions.FileOperations
{
	/// <summary>
	/// The per-item result of a file operation.
	/// </summary>
	/// <param name="Source">The path that was operated on.</param>
	/// <param name="Status">The outcome.</param>
	/// <param name="ResultPath">The final path of the item (the destination, which may be a generated unique name), if any.</param>
	/// <param name="ErrorKind">The failure category when <paramref name="Status"/> is <see cref="FileOperationStatus.Failed"/>.</param>
	/// <param name="ErrorMessage">A diagnostic message for failures.</param>
	/// <param name="ErrorPath">The path (possibly nested inside <paramref name="Source"/>) that caused the failure.</param>
	public sealed record FileOperationItemResult(
		string Source,
		FileOperationStatus Status,
		string? ResultPath = null,
		FileOperationErrorKind? ErrorKind = null,
		string? ErrorMessage = null,
		string? ErrorPath = null)
	{
		/// <summary>Gets whether the item was processed successfully.</summary>
		public bool Succeeded => Status == FileOperationStatus.Succeeded;
	}
}
