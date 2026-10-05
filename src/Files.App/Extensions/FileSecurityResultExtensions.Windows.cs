// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.Win32.Foundation;

namespace Files.App.Extensions
{
	/// <summary>
	/// Converts Win32 error codes from the security APIs to <see cref="FileSecurityResult"/>.
	/// </summary>
	public static class FileSecurityResultExtensions
	{
		/// <summary>
		/// Converts a Win32 error code to a <see cref="FileSecurityResult"/>; unknown errors map to <see cref="FileSecurityResult.Failed"/>.
		/// </summary>
		public static FileSecurityResult ToFileSecurityResult(this WIN32_ERROR error)
		{
			return error switch
			{
				WIN32_ERROR.ERROR_SUCCESS => FileSecurityResult.Success,
				WIN32_ERROR.ERROR_ACCESS_DENIED or WIN32_ERROR.ERROR_PRIVILEGE_NOT_HELD => FileSecurityResult.AccessDenied,
				WIN32_ERROR.ERROR_FILE_NOT_FOUND or WIN32_ERROR.ERROR_PATH_NOT_FOUND => FileSecurityResult.NotFound,
				WIN32_ERROR.ERROR_WRITE_PROTECT => FileSecurityResult.ReadOnly,
				WIN32_ERROR.ERROR_INVALID_ACL or WIN32_ERROR.ERROR_INVALID_SECURITY_DESCR => FileSecurityResult.InvalidAcl,
				WIN32_ERROR.ERROR_NOT_SUPPORTED or WIN32_ERROR.ERROR_CALL_NOT_IMPLEMENTED => FileSecurityResult.NotSupported,
				_ => FileSecurityResult.Failed,
			};
		}
	}
}
