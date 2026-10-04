// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Desktop (Linux) variant; the Windows one uses Win2D/System.Drawing/WinRT thumbnails.
	/// </summary>
	public static class FontFileHelper
	{
		private static readonly HashSet<string> _fontExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".ttf", ".otf", ".ttc", ".woff", ".woff2", ".fon",
		};

		public static bool IsFontFile(string? path)
			=> !string.IsNullOrEmpty(path) && _fontExtensions.Contains(SystemIO.Path.GetExtension(path));

		public static Task<byte[]?> GetWinRTThumbnailAsync(string fontPath, uint size)
		{
			// LINUX-TODO(thumbnails): font thumbnails (e.g. via SkiaSharp SKTypeface rendering)
			return Task.FromResult<byte[]?>(null);
		}

		public static byte[]? GenerateFontThumbnail(string fontPath, int size)
		{
			// LINUX-TODO(thumbnails): font thumbnails (e.g. via SkiaSharp SKTypeface rendering)
			return null;
		}
	}
}
