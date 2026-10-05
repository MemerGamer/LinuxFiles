// Copyright (c) Files Community
// Licensed under the MIT License.

using System;

namespace Files.Platform.Abstractions.Permissions
{
	/// <summary>
	/// Reads and changes the portable "attributes" of a file: hidden, read-only and the modification time.
	/// </summary>
	/// <remarks>
	/// Windows keeps these as file attribute flags. On Linux, hidden is a leading dot in the name (so toggling it renames the entry)
	/// and read-only means that no write permission bit is set.
	/// </remarks>
	public interface IFileAttributesService
	{
		/// <summary>
		/// Gets whether toggling the hidden state renames the entry. Callers should ask the user to confirm first.
		/// </summary>
		bool HiddenRequiresRename { get; }

		/// <summary>
		/// Gets whether the entry is hidden, which on Linux means that its name starts with a dot.
		/// </summary>
		bool IsHidden(string path);

		/// <summary>
		/// Gets <paramref name="fileName"/> adjusted to the requested hidden state, without touching the file system.
		/// </summary>
		/// <exception cref="ArgumentException">The result would be an empty name.</exception>
		string GetNameWithHiddenState(string fileName, bool hidden);

		/// <summary>
		/// Makes the entry hidden or visible by renaming it, and returns the new path.
		/// Throws when the target name exists or the rename is not permitted.
		/// </summary>
		string SetHidden(string path, bool hidden);

		/// <summary>
		/// Gets whether the entry is read-only (no write permission bit set), or <see langword="false"/> when it cannot be inspected.
		/// </summary>
		bool TryGetReadOnly(string path, out bool isReadOnly);

		/// <summary>
		/// Turns the read-only state on (clears every write bit) or off (restores the owner write bit).
		/// Throws when the current user may not change the permissions.
		/// </summary>
		void SetReadOnly(string path, bool isReadOnly);

		/// <summary>
		/// Sets the modification time of a file or directory.
		/// </summary>
		void SetModified(string path, DateTimeOffset modified);
	}
}
