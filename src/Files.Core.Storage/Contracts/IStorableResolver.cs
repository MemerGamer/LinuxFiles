// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Core.Storage.Contracts
{
	/// <summary>
	/// Resolves paths to <see cref="IStorable"/> instances by dispatching to the registered <see cref="IStorableRoute"/>s.
	/// </summary>
	/// <remarks>
	/// The first route, in ascending <see cref="IStorableRoute.Order"/>, whose <see cref="IStorableRoute.CanResolve(string)"/>
	/// returns <see langword="true"/> handles the path; later routes are not consulted, even if that route returns <see langword="null"/>.
	/// </remarks>
	public interface IStorableResolver
	{
		/// <summary>
		/// Determines whether any registered route handles <paramref name="path"/>.
		/// </summary>
		/// <remarks>This only inspects the string and does not perform any I/O, so the item may still not exist.</remarks>
		/// <param name="path">The path to inspect.</param>
		/// <returns><see langword="true"/> if a route handles <paramref name="path"/>; otherwise, <see langword="false"/>.</returns>
		bool CanResolve(string path);

		/// <summary>
		/// Gets the storable at <paramref name="path"/>.
		/// </summary>
		/// <param name="path">The path to resolve.</param>
		/// <param name="cancellationToken">A token that cancels the operation.</param>
		/// <returns>
		/// An <see cref="IFile"/> or <see cref="IFolder"/>, or <see langword="null"/> if no route handles the path,
		/// or it does not exist or cannot be accessed.
		/// </returns>
		/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
		Task<IStorable?> TryGetAsync(string path, CancellationToken cancellationToken = default);
	}
}
