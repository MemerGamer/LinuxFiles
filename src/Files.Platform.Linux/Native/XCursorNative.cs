// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;

namespace Files.Platform.Linux.Native
{
	internal static partial class XCursorNative
	{
		[LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
		private static partial int SetNativeEnvironment(string name, string value, int overwrite);

		[LibraryImport("libXcursor.so.1", StringMarshalling = StringMarshalling.Utf8)]
		private static partial nuint XcursorLibraryLoadCursor(nint display, string name);

		[LibraryImport("libX11.so.6")]
		private static partial void XrmInitialize();

		[LibraryImport("libX11.so.6")]
		private static partial nint XrmGetStringDatabase(nint resources);

		[LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
		private static partial int XrmGetResource(nint database, string name, string resourceClass, out nint type, out XrmValue value);

		[LibraryImport("libX11.so.6")]
		private static partial void XrmDestroyDatabase(nint database);

		[StructLayout(LayoutKind.Sequential)]
		private struct XrmValue
		{
			public uint Size;
			public nint Address;
		}

		internal static void SetEnvironmentIfAbsent(string name, string value)
		{
			if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
				return;
			// .NET's environment is separate from the libc environment used by libXcursor.
			if (SetNativeEnvironment(name, value, 1) == 0)
				Environment.SetEnvironmentVariable(name, value);
		}

		internal static nuint LoadCursor(nint display, string name, uint coreShape)
		{
			try
			{
				var cursor = XcursorLibraryLoadCursor(display, name);
				if (cursor != 0)
					return cursor;
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
			{
				// Xcursor is optional; retain the core X11 cursor when unavailable.
			}
			return X11WindowChromeNative.XCreateFontCursor(display, coreShape);
		}

		internal static (string? Theme, string? Size) ReadResources()
		{
			if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
				return (null, null);
			try
			{
				X11Native.XInitThreads();
				var display = X11Native.XOpenDisplay(0);
				if (display == 0)
					return (null, null);
				try
				{
					var resources = X11Native.XResourceManagerString(display);
					if (resources == 0)
						return (null, null);
					XrmInitialize();
					var database = XrmGetStringDatabase(resources);
					if (database == 0)
						return (null, null);
					try
					{
						return (ReadResource(database, "Xcursor.theme", "Xcursor.Theme"),
							ReadResource(database, "Xcursor.size", "Xcursor.Size"));
					}
					finally
					{
						XrmDestroyDatabase(database);
					}
				}
				finally
				{
					X11Native.XCloseDisplay(display);
				}
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
			{
				return (null, null);
			}
		}

		private static string? ReadResource(nint database, string name, string resourceClass)
		{
			if (XrmGetResource(database, name, resourceClass, out _, out var value) == 0)
				return null;
			// An invalid explicit resource must not be replaced by GNOME's preference.
			return value.Address != 0 && value.Size is > 0 and <= 512
				? Marshal.PtrToStringUTF8(value.Address, (int)value.Size - 1) : "\0";
		}
	}
}
