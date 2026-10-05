// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Core.Storage.Enums
{
	/// <summary>
	/// Defines the outcome of resolving a path with an <see cref="Contracts.IStorableRoute"/> or <see cref="Contracts.IStorableResolver"/>.
	/// </summary>
	public enum StorableStatus
	{
		/// <summary>
		/// The route does not handle this path, so the resolver tries the next route.
		/// From a resolver, no route handles the path. This is the default value.
		/// </summary>
		NotMine = 0,

		/// <summary>
		/// The path was resolved; <see cref="Contracts.StorableResult.Item"/> is set.
		/// </summary>
		Success,

		/// <summary>
		/// The route handles the path, but the item does not exist.
		/// </summary>
		NotFound,

		/// <summary>
		/// The route handles the path, but the item or one of its parents cannot be accessed.
		/// </summary>
		AccessDenied,

		/// <summary>
		/// The route handles the path, but resolving it failed for another reason.
		/// </summary>
		Error,
	}
}
