// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Security.Cryptography;
using System.Text;

namespace Files.Platform.Linux.Thumbnails
{
	/// <summary>
	/// Implements the naming rules of the XDG Thumbnail Managing Standard.
	/// </summary>
	public static class XdgThumbnailNaming
	{
		// Matches GLib's g_filename_to_uri so entries written by other file managers are shared.
		private const string AllowedPathChars = "-._~!$&'()*+,;=:@/";

		/// <summary>
		/// Gets the cache sub-directory name for a requested pixel size.
		/// </summary>
		public static string GetBucketName(uint requestedSize) => requestedSize switch
		{
			<= 128 => "normal",
			<= 256 => "large",
			<= 512 => "x-large",
			_ => "xx-large",
		};

		/// <summary>
		/// Gets the maximum pixel size of a bucket.
		/// </summary>
		public static int GetBucketSize(string bucket) => bucket switch
		{
			"normal" => 128,
			"large" => 256,
			"x-large" => 512,
			_ => 1024,
		};

		/// <summary>
		/// Converts an absolute path to a percent-encoded <c>file://</c> URI.
		/// </summary>
		public static string ToFileUri(string absolutePath)
		{
			var sb = new StringBuilder("file://", 7 + absolutePath.Length + 16);
			foreach (var b in Encoding.UTF8.GetBytes(absolutePath))
			{
				var c = (char)b;
				if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || AllowedPathChars.Contains(c)))
					sb.Append(c);
				else
					sb.Append('%').Append(b.ToString("X2"));
			}

			return sb.ToString();
		}

		/// <summary>
		/// Gets the lower-case hex MD5 of the URI, used as the thumbnail file name without extension.
		/// </summary>
		public static string GetHash(string uri)
		{
			return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(uri)));
		}
	}
}
