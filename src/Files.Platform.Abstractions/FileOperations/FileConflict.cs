// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Abstractions.FileOperations
{
	/// <summary>
	/// Describes an item whose destination already exists.
	/// </summary>
	/// <param name="Source">The source path.</param>
	/// <param name="Destination">The existing destination path.</param>
	/// <param name="SourceIsDirectory">Whether the source is a folder.</param>
	/// <param name="DestinationIsDirectory">Whether the existing destination is a folder.</param>
	public sealed record FileConflict(
		string Source,
		string Destination,
		bool SourceIsDirectory,
		bool DestinationIsDirectory);
}
