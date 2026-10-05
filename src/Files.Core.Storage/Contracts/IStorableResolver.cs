// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Core.Storage.Enums;

namespace Files.Core.Storage.Contracts
{
	/// <summary>
	/// Resolves paths to <see cref="IStorable"/> instances by dispatching to the registered <see cref="IStorableRoute"/>s.
	/// </summary>
	/// <remarks>
	/// Routes are tried in ascending <see cref="IStorableRoute.Order"/>. A route that returns <see cref="StorableStatus.NotMine"/>
	/// passes the path to the next route; the first other result is returned.
	/// </remarks>
	public interface IStorableResolver
	{
		/// <summary>
		/// Resolves <paramref name="path"/>.
		/// </summary>
		/// <param name="path">The path to resolve.</param>
		/// <param name="cancellationToken">A token that cancels the operation.</param>
		/// <returns>
		/// <see cref="StorableStatus.Success"/> with an <see cref="IFile"/> or <see cref="IFolder"/>; <see cref="StorableStatus.NotMine"/>
		/// if no route handles the path; otherwise, the reason the handling route could not resolve it.
		/// </returns>
		/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
		Task<StorableResult> TryGetAsync(string path, CancellationToken cancellationToken = default);
	}
}
