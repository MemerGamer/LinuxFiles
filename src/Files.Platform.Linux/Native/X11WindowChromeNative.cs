// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace Files.Platform.Linux.Native
{
	internal static partial class X11WindowChromeNative
	{
		[LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
		public static partial int XStoreName(nint display, nuint window, string title);

		[LibraryImport("libX11.so.6")]
		public static partial nuint XCreateWindow(nint display, nuint parent, int x, int y, uint width, uint height,
			uint borderWidth, int depth, uint windowClass, nint visual, nuint valueMask, nint attributes);

		[LibraryImport("libX11.so.6")]
		public static partial int XMoveResizeWindow(nint display, nuint window, int x, int y, uint width, uint height);

		[LibraryImport("libX11.so.6")]
		public static partial int XMapRaised(nint display, nuint window);

		[LibraryImport("libX11.so.6")]
		public static partial int XUnmapWindow(nint display, nuint window);

		[LibraryImport("libX11.so.6")]
		public static partial int XUngrabPointer(nint display, nuint time);

		[LibraryImport("libX11.so.6")]
		public static partial nuint XCreateFontCursor(nint display, uint shape);

		[LibraryImport("libX11.so.6")]
		public static partial int XDefineCursor(nint display, nuint window, nuint cursor);

		[LibraryImport("libX11.so.6")]
		public static partial int XFreeCursor(nint display, nuint cursor);
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct XChromePointerEvent
	{
		public int Type;
		public nuint Serial;
		public int SendEvent;
		public nint Display;
		public nuint Window;
		public nuint Root;
		public nuint Subwindow;
		public nuint Time;
		public int X;
		public int Y;
		public int RootX;
		public int RootY;
		public uint State;
		public uint Button;
		public int SameScreen;
	}
}
