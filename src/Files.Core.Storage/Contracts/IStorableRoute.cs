// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Core.Storage.Enums;

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
		/// Resolves <paramref name="path"/>.
		/// </summary>
		/// <remarks>
		/// Return <see cref="StorableResult.NotMine"/> as cheaply as possible for paths this route does not handle, so that the
		/// resolver tries the next route. A route may inspect the file system to decide (for example, to tell an archive from a
		/// directory named <c>x.zip</c>). Any other result is final.
		/// </remarks>
		/// <param name="path">The path to resolve.</param>
		/// <param name="cancellationToken">A token that cancels the operation.</param>
		/// <returns>
		/// <see cref="StorableStatus.Success"/> with an <see cref="IFile"/> or <see cref="IFolder"/>; <see cref="StorableStatus.NotMine"/>
		/// if this route does not handle the path; otherwise, the reason the handled path could not be resolved.
		/// </returns>
		/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
		Task<StorableResult> TryGetAsync(string path, CancellationToken cancellationToken = default);
	}
}
