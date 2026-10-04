// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Files.Platform.Linux.Native
{
	/// <summary>
	/// Registers bundled font files with fontconfig for this process so Skia resolves them by family name and weight.
	/// </summary>
	public static partial class FontConfigNative
	{
		[LibraryImport("libfontconfig.so.1", StringMarshalling = StringMarshalling.Utf8)]
		[return: MarshalAs(UnmanagedType.I1)]
		private static partial bool FcConfigAppFontAddFile(nint config, string file);

		/// <summary>
		/// Adds every <c>*.ttf</c>/<c>*.otf</c> file in <paramref name="directory"/> to the process font set. Failures are ignored.
		/// </summary>
		/// <returns>The number of files registered.</returns>
		public static int RegisterFontDirectory(string directory)
		{
			var count = 0;
			try
			{
				if (!Directory.Exists(directory))
					return 0;

				foreach (var file in Directory.EnumerateFiles(directory))
				{
					if ((file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
						&& FcConfigAppFontAddFile(0, file))
						count++;
				}
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or IOException or UnauthorizedAccessException)
			{
			}

			return count;
		}
	}
}
