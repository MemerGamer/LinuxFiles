// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Abstractions.Trash
{
	/// <summary>
	/// Describes the outcome of a trash operation on a single item.
	/// </summary>
	/// <param name="Source">The path or trash path of the item the operation was applied to.</param>
	/// <param name="Succeeded">Whether the operation succeeded.</param>
	/// <param name="ErrorMessage">The failure reason, or <see langword="null"/> on success.</param>
	/// <param name="ResultPath">The restored path, or the path inside the trash for a trash operation.</param>
	/// <param name="Item">The trash entry created by a trash operation.</param>
	public sealed record TrashOperationResult(
		string Source,
		bool Succeeded,
		string? ErrorMessage = null,
		string? ResultPath = null,
		TrashItem? Item = null)
	{
		/// <summary>
		/// Creates a successful result.
		/// </summary>
		public static TrashOperationResult Success(string source, string? resultPath = null, TrashItem? item = null)
			=> new(source, true, null, resultPath, item);

		/// <summary>
		/// Creates a failed result.
		/// </summary>
		public static TrashOperationResult Failure(string source, string errorMessage)
			=> new(source, false, errorMessage);
	}
}
