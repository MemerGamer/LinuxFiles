// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Native;
using System;
using System.Threading;
using System.Text;

namespace Files.Platform.Linux.Windowing
{
	/// <summary>Delegates client title bar gestures to the X11 window manager.</summary>
	public sealed unsafe class X11WindowChrome : IDisposable
	{
		private readonly nint _display;
		private readonly object _sync = new();
		private readonly nuint[] _grips = new nuint[9];
		private Thread? _eventThread;
		private bool _disposed;
		private bool _dragPending;
		private int _pressX, _pressY;
		private nuint _lastClick;
		private readonly nuint _window;
		private readonly nuint _root;
		private readonly nuint _moveResize;
		private readonly nuint _state;
		private readonly nuint _maximizedHorizontal;
		private readonly nuint _maximizedVertical;

		private X11WindowChrome(nint display, nuint window)
		{
			_display = display;
			_window = window;
			_root = X11Native.XDefaultRootWindow(display);
			_moveResize = Atom("_NET_WM_MOVERESIZE");
			_state = Atom("_NET_WM_STATE");
			_maximizedHorizontal = Atom("_NET_WM_STATE_MAXIMIZED_HORZ");
			_maximizedVertical = Atom("_NET_WM_STATE_MAXIMIZED_VERT");
		}

		public static X11WindowChrome? TryCreate(nuint window)
		{
			if (window == 0)
				return null;

			X11Native.XInitThreads();
			var display = X11Native.XOpenDisplay(0);
			if (display == 0)
				return null;

			var chrome = new X11WindowChrome(display, window);
			chrome.SupportsClientSideDecorations = chrome.ContainsAtom(chrome._root, chrome.Atom("_NET_SUPPORTED"), chrome._moveResize);
			if (chrome.SupportsClientSideDecorations)
				chrome.CreateGrips();
			return chrome;
		}

		public bool SupportsClientSideDecorations { get; private set; }

		public void SetTitle(string title)
		{
			lock (_sync)
			{
				if (_disposed)
					return;
				title = SanitizeTitle(title);
				X11WindowChromeNative.XStoreName(_display, _window, title);
				var bytes = Encoding.UTF8.GetBytes(title);
				fixed (byte* data = bytes)
					X11Native.XChangeProperty(_display, _window, Atom("_NET_WM_NAME"), Atom("UTF8_STRING"), 8,
						X11Native.PropModeReplace, data, bytes.Length);
				X11Native.XFlush(_display);
			}
		}

		/// <summary>Replaces control characters (including NUL and line breaks) so a folder name cannot corrupt the window title.</summary>
		public static string SanitizeTitle(string title)
		{
			if (!title.AsSpan().ContainsAnyInRange('\0', '\x1F') && !title.AsSpan().ContainsAnyInRange('\x7F', '\x9F'))
				return title;

			var builder = new StringBuilder(title.Length);
			foreach (var c in title)
				builder.Append(char.IsControl(c) ? ' ' : c);
			return builder.ToString();
		}

		public void HideRegions()
		{
			lock (_sync)
			{
				if (_disposed || !SupportsClientSideDecorations)
					return;
				_dragPending = false;
				_lastClick = 0;
				foreach (var grip in _grips)
					X11WindowChromeNative.XUnmapWindow(_display, grip);
				X11Native.XFlush(_display);
			}
		}

		public bool IsMaximized
		{
			get
			{
				lock (_sync)
					return !_disposed && ContainsAtom(_window, _state, _maximizedHorizontal) &&
						ContainsAtom(_window, _state, _maximizedVertical);
			}
		}

		public void ToggleMaximize()
		{
			lock (_sync)
				if (!_disposed)
					Send(_state, IsMaximized ? 0 : 1, (nint)_maximizedHorizontal, (nint)_maximizedVertical, 1, 0);
		}

		private void CreateGrips()
		{
			uint[] cursors = [134, 138, 136, 96, 14, 16, 12, 70, 68];
			for (var i = 0; i < _grips.Length; i++)
			{
				// InputOnly siblings above Uno's rendering window own the implicit pointer grab.
				_grips[i] = X11WindowChromeNative.XCreateWindow(_display, _window, 0, 0, 1, 1, 0, 0, 2, 0, 0, 0);
				X11Native.XSelectInput(_display, _grips[i], (1 << 2) | (1 << 3) | (1 << 6));
				var cursor = X11WindowChromeNative.XCreateFontCursor(_display, cursors[i]);
				X11WindowChromeNative.XDefineCursor(_display, _grips[i], cursor);
				X11WindowChromeNative.XFreeCursor(_display, cursor);
			}
			_eventThread = new Thread(ReadPointerEvents) { IsBackground = true, Name = "X11 window chrome" };
			_eventThread.Start();
		}

		public void SetRegions(int width, int height, int dragX, int dragWidth, int titleHeight, int border)
		{
			lock (_sync)
			{
				if (_disposed || !SupportsClientSideDecorations || width <= border * 2 || height <= border * 2)
					return;

				SetGrip(8, dragX, border, dragWidth, titleHeight - border);
				var resize = IsMaximized ? 0 : border;
				SetGrip(0, 0, 0, resize, resize);
				SetGrip(1, border, 0, width - border * 2, resize);
				SetGrip(2, width - border, 0, resize, resize);
				SetGrip(3, width - border, border, resize, height - border * 2);
				SetGrip(4, width - border, height - border, resize, resize);
				SetGrip(5, border, height - border, width - border * 2, resize);
				SetGrip(6, 0, height - border, resize, resize);
				SetGrip(7, 0, border, resize, height - border * 2);
				X11Native.XFlush(_display);
			}
		}

		private void SetGrip(int index, int x, int y, int width, int height)
		{
			if (width <= 0 || height <= 0)
			{
				X11WindowChromeNative.XUnmapWindow(_display, _grips[index]);
				return;
			}
			X11WindowChromeNative.XMoveResizeWindow(_display, _grips[index], x, y, (uint)width, (uint)height);
			X11WindowChromeNative.XMapRaised(_display, _grips[index]);
		}

		private void ReadPointerEvents()
		{
			var fd = new PollFd { Fd = X11Native.XConnectionNumber(_display), Events = 1 };
			while (true)
			{
				lock (_sync)
				{
					if (_disposed)
						return;
					while (X11Native.XPending(_display) > 0)
					{
						XEvent ev;
						X11Native.XNextEvent(_display, &ev);
						HandlePointer(*(XChromePointerEvent*)&ev);
					}
				}
				X11Native.Poll(&fd, 1, 50);
			}
		}

		private void HandlePointer(XChromePointerEvent ev)
		{
			var direction = Array.IndexOf(_grips, ev.Window);
			if (direction < 0)
				return;

			if (ev.Type == 4 && ev.Button == 1)
			{
				if (direction != 8)
				{
					BeginMoveResize(direction, ev.RootX, ev.RootY, ev.Time);
					return;
				}
				if (_lastClick != 0 && ev.Time - _lastClick < 500 &&
					Math.Abs(ev.RootX - _pressX) + Math.Abs(ev.RootY - _pressY) < 5)
				{
					_dragPending = false;
					_lastClick = 0;
					ToggleMaximize();
					return;
				}
				_pressX = ev.RootX;
				_pressY = ev.RootY;
				_lastClick = ev.Time;
				_dragPending = true;
			}
			else if (ev.Type == 5 && ev.Button == 1)
				_dragPending = false;
			else if (ev.Type == 6 && _dragPending && (ev.State & X11Native.Button1Mask) != 0 &&
				Math.Abs(ev.RootX - _pressX) + Math.Abs(ev.RootY - _pressY) >= 4)
			{
				_dragPending = false;
				_lastClick = 0;
				BeginMoveResize(8, ev.RootX, ev.RootY, ev.Time);
			}
		}

		private void BeginMoveResize(int direction, int rootX, int rootY, nuint time)
		{
			X11WindowChromeNative.XUngrabPointer(_display, time);
			Send(_moveResize, rootX, rootY, direction, 1, 1);
		}

		private nuint Atom(string name) => X11Native.XInternAtom(_display, name, false);

		private bool ContainsAtom(nuint window, nuint property, nuint atom)
		{
			nuint type, count, remaining;
			int format;
			byte* data;
			var status = X11Native.XGetWindowProperty(_display, window, property, 0, 1024, false,
				Atom("ATOM"), &type, &format, &count, &remaining, &data);
			try
			{
				if (status != 0 || format != 32 || data == null)
					return false;

				for (nuint i = 0; i < count; i++)
					if (((nuint*)data)[i] == atom)
						return true;

				return false;
			}
			finally
			{
				if (data != null)
					X11Native.XFree(data);
			}
		}

		private void Send(nuint message, nint a, nint b, nint c, nint d, nint e)
		{
			var ev = new XEvent
			{
				ClientMessage = new XClientMessageEvent
				{
					Type = X11Native.ClientMessage,
					Display = _display,
					Window = _window,
					MessageType = message,
					Format = 32,
					L0 = a, L1 = b, L2 = c, L3 = d, L4 = e,
				}
			};
			X11Native.XSendEvent(_display, _root, false, (1 << 20) | (1 << 19), &ev);
			X11Native.XFlush(_display);
		}

		public void Dispose()
		{
			lock (_sync)
			{
				if (_disposed)
					return;
				_disposed = true;
			}
			_eventThread?.Join();
			X11Native.XCloseDisplay(_display);
		}
	}
}
