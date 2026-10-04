// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;

namespace Files.Platform.Linux.Native
{
	/// <summary>
	/// The Xlib subset used to own selections (clipboard, XDND) on a private connection. Window, atom and time values are
	/// <c>unsigned long</c> in C, which is 64 bits on both linux-x64 and linux-arm64.
	/// </summary>
	internal static unsafe partial class X11Native
	{
		private const string LibX11 = "libX11.so.6";

		public const int SelectionClear = 29;
		public const int SelectionRequest = 30;
		public const int SelectionNotify = 31;
		public const int PropertyNotify = 28;
		public const int ClientMessage = 33;

		public const int PropertyChangeMask = 1 << 22;

		public const int PropertyNewValue = 0;
		public const int PropertyDelete = 1;

		public const int PropModeReplace = 0;

		public const int ShiftMask = 1;
		public const int ControlMask = 4;
		public const int Button1Mask = 1 << 8;

		[LibraryImport(LibX11)]
		public static partial int XInitThreads();

		[LibraryImport(LibX11)]
		public static partial nint XOpenDisplay(nint name);

		[LibraryImport(LibX11)]
		public static partial int XCloseDisplay(nint display);

		[LibraryImport(LibX11)]
		public static partial int XConnectionNumber(nint display);

		[LibraryImport(LibX11)]
		public static partial nuint XDefaultRootWindow(nint display);

		[LibraryImport(LibX11)]
		public static partial nuint XCreateSimpleWindow(nint display, nuint parent, int x, int y, uint width, uint height, uint borderWidth, nuint border, nuint background);

		[LibraryImport(LibX11)]
		public static partial int XDestroyWindow(nint display, nuint window);

		[LibraryImport(LibX11, StringMarshalling = StringMarshalling.Utf8)]
		public static partial nuint XInternAtom(nint display, string name, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

		[LibraryImport(LibX11)]
		public static partial int XSelectInput(nint display, nuint window, nint eventMask);

		[LibraryImport(LibX11)]
		public static partial int XSetSelectionOwner(nint display, nuint selection, nuint owner, nuint time);

		[LibraryImport(LibX11)]
		public static partial nuint XGetSelectionOwner(nint display, nuint selection);

		[LibraryImport(LibX11)]
		public static partial int XConvertSelection(nint display, nuint selection, nuint target, nuint property, nuint requestor, nuint time);

		[LibraryImport(LibX11)]
		public static partial int XPending(nint display);

		[LibraryImport(LibX11)]
		public static partial int XNextEvent(nint display, XEvent* ev);

		[LibraryImport(LibX11)]
		public static partial int XFlush(nint display);

		[LibraryImport(LibX11)]
		public static partial int XSync(nint display, [MarshalAs(UnmanagedType.Bool)] bool discard);

		[LibraryImport(LibX11)]
		public static partial int XSendEvent(nint display, nuint window, [MarshalAs(UnmanagedType.Bool)] bool propagate, nint eventMask, XEvent* ev);

		[LibraryImport(LibX11)]
		public static partial int XChangeProperty(nint display, nuint window, nuint property, nuint type, int format, int mode, byte* data, int elements);

		[LibraryImport(LibX11)]
		public static partial int XDeleteProperty(nint display, nuint window, nuint property);

		[LibraryImport(LibX11)]
		public static partial int XGetWindowProperty(nint display, nuint window, nuint property, nint offset, nint length, [MarshalAs(UnmanagedType.Bool)] bool delete,
			nuint requestedType, nuint* actualType, int* actualFormat, nuint* itemCount, nuint* bytesAfter, byte** data);

		[LibraryImport(LibX11)]
		public static partial int XFree(void* data);

		[LibraryImport(LibX11)]
		public static partial nint XMaxRequestSize(nint display);

		[LibraryImport(LibX11)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static partial bool XQueryPointer(nint display, nuint window, nuint* rootReturn, nuint* childReturn, int* rootX, int* rootY, int* winX, int* winY, uint* mask);

		[LibraryImport(LibX11)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static partial bool XTranslateCoordinates(nint display, nuint source, nuint destination, int x, int y, int* destX, int* destY, nuint* child);

		[LibraryImport(LibX11)]
		public static partial nint XSetErrorHandler(nint handler);

		[LibraryImport("libc", EntryPoint = "pipe2", SetLastError = true)]
		public static partial int Pipe2(int* fds, int flags);

		[LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
		public static partial int Poll(PollFd* fds, ulong count, int timeoutMilliseconds);

		[LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
		public static partial nint Read(int fd, byte* buffer, nuint count);

		[LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
		public static partial nint Write(int fd, byte* buffer, nuint count);

		[LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
		public static partial int Close(int fd);

		[LibraryImport("libc", EntryPoint = "getpid")]
		public static partial int GetPid();
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct PollFd
	{
		public int Fd;
		public short Events;
		public short Revents;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct XAnyEvent
	{
		public int Type;
		public nuint Serial;
		public int SendEvent;
		public nint Display;
		public nuint Window;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct XSelectionRequestEvent
	{
		public int Type;
		public nuint Serial;
		public int SendEvent;
		public nint Display;
		public nuint Owner;
		public nuint Requestor;
		public nuint Selection;
		public nuint Target;
		public nuint Property;
		public nuint Time;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct XSelectionEvent
	{
		public int Type;
		public nuint Serial;
		public int SendEvent;
		public nint Display;
		public nuint Requestor;
		public nuint Selection;
		public nuint Target;
		public nuint Property;
		public nuint Time;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct XSelectionClearEvent
	{
		public int Type;
		public nuint Serial;
		public int SendEvent;
		public nint Display;
		public nuint Window;
		public nuint Selection;
		public nuint Time;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct XPropertyEvent
	{
		public int Type;
		public nuint Serial;
		public int SendEvent;
		public nint Display;
		public nuint Window;
		public nuint Atom;
		public nuint Time;
		public int State;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct XClientMessageEvent
	{
		public int Type;
		public nuint Serial;
		public int SendEvent;
		public nint Display;
		public nuint Window;
		public nuint MessageType;
		public int Format;
		public nint L0;
		public nint L1;
		public nint L2;
		public nint L3;
		public nint L4;
	}

	/// <summary>The Xlib <c>XEvent</c> union (24 longs).</summary>
	[StructLayout(LayoutKind.Explicit, Size = 192)]
	internal struct XEvent
	{
		[FieldOffset(0)] public int Type;
		[FieldOffset(0)] public XAnyEvent Any;
		[FieldOffset(0)] public XSelectionRequestEvent SelectionRequest;
		[FieldOffset(0)] public XSelectionEvent Selection;
		[FieldOffset(0)] public XSelectionClearEvent SelectionClear;
		[FieldOffset(0)] public XPropertyEvent Property;
		[FieldOffset(0)] public XClientMessageEvent ClientMessage;
	}
}
