// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.App.Data.Contracts
{
	/// <summary>
	/// Provides service to manage storage security objects on NTFS and ReFS. Only registered on Windows.
	/// </summary>
	public interface IStorageSecurityService
	{
		/// <summary>
		/// Get the owner of the object specified by the path.
		/// </summary>
		/// <param name="path">The file full path</param>
		/// <returns>The SID string of the owner</returns>
		string GetOwner(string path);

		/// <summary>
		/// Set the owner of the object specified by the path.
		/// </summary>
		/// <param name="path">The file full path</param>
		/// <param name="sid">The owner security identifier (SID)</param>
		/// <returns></returns>
		bool SetOwner(string path, string sid);

		/// <summary>
		/// Get information about an access control list (ACL).
		/// </summary>
		/// <param name="path"></param>
		/// <param name="isFolder"></param>
		/// <returns>The outcome; on success <paramref name="acl"/> holds the list.</returns>
		FileSecurityResult GetAcl(string path, bool isFolder, out AccessControlList acl);

		/// <summary>
		/// Add an default Access Control Entry (ACE) to the specified object's DACL
		/// </summary>
		/// <param name="path">The object's path to add an new ACE to its DACL</param>
		/// <param name="sid">Principal's SID</param>
		/// <returns> <see cref="FileSecurityResult.Success"/> when the entry was added; otherwise the reason it failed.</returns>
		FileSecurityResult AddAce(string szPath, bool isFolder, string szSid);

		/// <summary>
		/// Add an Access Control Entry (ACE) from the specified object's DACL
		/// </summary>
		/// <param name="szPath">The object's path to remove an ACE from its DACL</param>
		/// <param name="dwAceIndex"></param>
		/// <returns></returns>
		FileSecurityResult DeleteAce(string szPath, uint dwAceIndex);
	}
}
