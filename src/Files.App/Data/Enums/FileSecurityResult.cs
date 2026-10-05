// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Data.Enums
{
	/// <summary>
	/// Defines the outcome of reading or changing a file's security settings (ACL or POSIX permissions).
	/// </summary>
	public enum FileSecurityResult
	{
		/// <summary>
		/// The operation succeeded.
		/// </summary>
		Success,

		/// <summary>
		/// The caller lacks the rights to read or change the security settings.
		/// </summary>
		AccessDenied,

		/// <summary>
		/// The security descriptor or access control list is invalid.
		/// </summary>
		InvalidAcl,

		/// <summary>
		/// The file system or platform does not support the operation.
		/// </summary>
		NotSupported,

		/// <summary>
		/// The operation failed for another reason.
		/// </summary>
		Failed,
	}
}
