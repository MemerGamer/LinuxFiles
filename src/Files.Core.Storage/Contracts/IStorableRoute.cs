// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Core.Storage.Contracts
{
	/// <summary>
	/// Resolves one kind of path (local file system, archive entry, FTP, ...) to an <see cref="IStorable"/>.
	/// Routes are consulted by an <see cref="IStorableResolver"/> in ascending <see cref="Order"/>.
	/// </summary>
	public interface IStorableRoute
	{
		/// <summary>
		/// Gets the position of this route relative to other routes. Lower values are consulted first,
		/// so specific routes (such as archives) must use a lower value than catch-all routes (such as the local file system).
		/// Routes with equal values keep their registration order.
		/// </summary>
		int Order { get; }

		/// <summary>
		/// Determines whether this route handles <paramref name="path"/>.
		/// </summary>
		/// <remarks>
		/// Implementations must only inspect the string and must not perform any I/O.
		/// A route that claims a path is authoritative for it.
		/// </remarks>
		/// <param name="path">The path to inspect.</param>
		/// <returns><see langword="true"/> if this route handles <paramref name="path"/>; otherwise, <see langword="false"/>.</returns>
		bool CanResolve(string path);

		/// <summary>
		/// Gets the storable at <paramref name="path"/>.
		/// </summary>
		/// <param name="path">A path for which <see cref="CanResolve(string)"/> returned <see langword="true"/>.</param>
		/// <param name="cancellationToken">A token that cancels the operation.</param>
		/// <returns>
		/// An <see cref="IFile"/> or <see cref="IFolder"/>, or <see langword="null"/> if the path is not handled by this route,
		/// does not exist or cannot be accessed.
		/// </returns>
		/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
		Task<IStorable?> TryGetAsync(string path, CancellationToken cancellationToken = default);
	}
}
