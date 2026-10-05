// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Core.Storage.Enums;
using System.Diagnostics.CodeAnalysis;

namespace Files.Core.Storage.Contracts
{
	/// <summary>
	/// The result of resolving a path: a <see cref="StorableStatus"/> and, on success, the resolved <see cref="IStorable"/>.
	/// </summary>
	/// <remarks><see cref="Item"/> is non-null exactly when <see cref="Status"/> is <see cref="StorableStatus.Success"/>.</remarks>
	public readonly record struct StorableResult
	{
		/// <summary>
		/// Gets the result for a path the route does not handle. This is also the <see langword="default"/> value.
		/// </summary>
		public static StorableResult NotMine => default;

		/// <summary>
		/// Gets the result for a handled path whose item does not exist.
		/// </summary>
		public static StorableResult NotFound => new(StorableStatus.NotFound, null);

		/// <summary>
		/// Gets the result for a handled path that cannot be accessed.
		/// </summary>
		public static StorableResult AccessDenied => new(StorableStatus.AccessDenied, null);

		/// <summary>
		/// Gets the result for a handled path that failed to resolve for another reason.
		/// </summary>
		public static StorableResult Error => new(StorableStatus.Error, null);

		/// <summary>
		/// Gets the outcome of the resolution.
		/// </summary>
		public StorableStatus Status { get; }

		/// <summary>
		/// Gets the resolved <see cref="IFile"/> or <see cref="IFolder"/>, or <see langword="null"/> if the resolution did not succeed.
		/// </summary>
		public IStorable? Item { get; }

		/// <summary>
		/// Gets a value indicating whether <see cref="Status"/> is <see cref="StorableStatus.Success"/>.
		/// </summary>
		[MemberNotNullWhen(true, nameof(Item))]
		public bool IsSuccess => Item is not null;

		private StorableResult(StorableStatus status, IStorable? item)
		{
			Status = status;
			Item = item;
		}

		/// <summary>
		/// Creates a successful result.
		/// </summary>
		/// <param name="item">The resolved storable.</param>
		/// <returns>A result with <see cref="StorableStatus.Success"/>.</returns>
		public static StorableResult Success(IStorable item)
		{
			ArgumentNullException.ThrowIfNull(item);
			return new(StorableStatus.Success, item);
		}
	}
}
