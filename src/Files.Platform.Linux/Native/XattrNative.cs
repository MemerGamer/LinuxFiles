// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;

namespace Files.Platform.Linux.Native
{
	/// <summary>
	/// Extended attribute calls (<c>getxattr</c>, <c>setxattr</c>, <c>removexattr</c>, <c>listxattr</c>).
	/// </summary>
	internal static unsafe partial class XattrNative
	{
		private const int ENODATA = 61;
		private const int ERANGE = 34;

		[LibraryImport("libc", EntryPoint = "getxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
		private static partial nint GetXattr(string path, string name, byte* value, nuint size);

		[LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
		private static partial int SetXattr(string path, string name, byte* value, nuint size, int flags);

		[LibraryImport("libc", EntryPoint = "removexattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
		private static partial int RemoveXattr(string path, string name);

		[LibraryImport("libc", EntryPoint = "listxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
		private static partial nint ListXattr(string path, byte* list, nuint size);

		/// <summary>
		/// Reads an attribute. Returns null if it does not exist or cannot be read; <paramref name="errno"/> tells why.
		/// </summary>
		public static byte[]? Get(string path, string name, out int errno)
		{
			errno = 0;
			var buffer = new byte[256];
			while (true)
			{
				nint length;
				fixed (byte* p = buffer)
					length = GetXattr(path, name, p, (nuint)buffer.Length);

				if (length >= 0)
					return buffer.AsSpan(0, (int)length).ToArray();

				errno = Marshal.GetLastPInvokeError();
				if (errno != ERANGE || buffer.Length >= 1 << 20)
					return null;

				buffer = new byte[buffer.Length * 8];
			}
		}

		/// <summary>
		/// Writes an attribute. Returns false on failure (<paramref name="errno"/> tells why, e.g. ENOTSUP when the file system has no xattr support).
		/// </summary>
		public static bool Set(string path, string name, ReadOnlySpan<byte> value, out int errno)
		{
			errno = 0;
			int result;
			fixed (byte* p = value)
				result = SetXattr(path, name, p, (nuint)value.Length, 0);

			if (result == 0)
				return true;

			errno = Marshal.GetLastPInvokeError();
			return false;
		}

		/// <summary>
		/// Removes an attribute. A missing attribute counts as success.
		/// </summary>
		public static bool Remove(string path, string name, out int errno)
		{
			errno = 0;
			if (RemoveXattr(path, name) == 0)
				return true;

			errno = Marshal.GetLastPInvokeError();
			if (errno == ENODATA)
			{
				errno = 0;
				return true;
			}

			return false;
		}

		/// <summary>
		/// Lists the attribute names of a file, or null on failure.
		/// </summary>
		public static string[]? List(string path, out int errno)
		{
			errno = 0;
			var buffer = new byte[1024];
			while (true)
			{
				nint length;
				fixed (byte* p = buffer)
					length = ListXattr(path, p, (nuint)buffer.Length);

				if (length >= 0)
				{
					return System.Text.Encoding.UTF8.GetString(buffer, 0, (int)length)
						.Split('\0', StringSplitOptions.RemoveEmptyEntries);
				}

				errno = Marshal.GetLastPInvokeError();
				if (errno != ERANGE || buffer.Length >= 1 << 20)
					return null;

				buffer = new byte[buffer.Length * 8];
			}
		}
	}
}
