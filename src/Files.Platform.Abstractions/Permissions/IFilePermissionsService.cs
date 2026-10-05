// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Permissions
{
	/// <summary>
	/// The ownership and permission bits of a file system entry, read without following symbolic links.
	/// </summary>
	/// <param name="Mode">The permission bits including setuid, setgid and sticky.</param>
	/// <param name="OwnerId">The numeric owner.</param>
	/// <param name="GroupId">The numeric group.</param>
	/// <param name="OwnerName">The owner name, or the numeric id when it has no name.</param>
	/// <param name="GroupName">The group name, or the numeric id when it has no name.</param>
	/// <param name="IsDirectory">Whether the entry is a directory.</param>
	/// <param name="IsSymbolicLink">Whether the entry is a symbolic link; the bits of a link cannot be changed.</param>
	/// <param name="CanChangeMode">Whether the current user may change the permission bits.</param>
	/// <param name="CanChangeOwner">Whether the current user may change the owner and group.</param>
	public sealed record FilePermissionsInfo(
		UnixFileMode Mode,
		uint OwnerId,
		uint GroupId,
		string OwnerName,
		string GroupName,
		bool IsDirectory,
		bool IsSymbolicLink,
		bool CanChangeMode,
		bool CanChangeOwner);

	/// <summary>
	/// The outcome of a recursive permission change.
	/// </summary>
	/// <param name="Changed">The number of entries that were updated.</param>
	/// <param name="Failed">The number of entries that could not be updated.</param>
	public readonly record struct PermissionsApplyResult(int Changed, int Failed);

	/// <summary>
	/// Reads and changes POSIX permissions and ownership.
	/// </summary>
	public interface IFilePermissionsService
	{
		/// <summary>
		/// Gets the permissions of <paramref name="path"/>, or <see langword="false"/> when the entry cannot be inspected.
		/// </summary>
		bool TryGetPermissions(string path, out FilePermissionsInfo info);

		/// <summary>
		/// Replaces the permission bits of <paramref name="path"/>. Throws when the change is not permitted.
		/// </summary>
		void SetMode(string path, UnixFileMode mode);

		/// <summary>
		/// Sets and clears permission bits on a directory and everything below it. Symbolic links are skipped and never followed,
		/// and every descendant is addressed relative to an open directory handle so a swapped path component cannot redirect the change.
		/// </summary>
		/// <param name="directoryPath">The directory to start from.</param>
		/// <param name="setBits">The bits to turn on for every entry.</param>
		/// <param name="clearBits">The bits to turn off for every entry.</param>
		/// <param name="cancellationToken">Cancels the walk.</param>
		Task<PermissionsApplyResult> SetModeRecursiveAsync(string directoryPath, UnixFileMode setBits, UnixFileMode clearBits, CancellationToken cancellationToken = default);

		/// <summary>
		/// Changes the owner and/or group of <paramref name="path"/> (symbolic links are not followed). Pass <see langword="null"/> to keep a value.
		/// Throws when the current user is not permitted to do so.
		/// </summary>
		void SetOwner(string path, uint? ownerId, uint? groupId);

		/// <summary>
		/// Resolves a user name to its numeric id, or null when unknown.
		/// </summary>
		uint? FindUserId(string name);

		/// <summary>
		/// Resolves a group name to its numeric id, or null when unknown.
		/// </summary>
		uint? FindGroupId(string name);
	}
}
