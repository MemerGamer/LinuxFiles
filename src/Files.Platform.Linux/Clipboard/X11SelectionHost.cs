// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Clipboard;
using Files.Platform.Linux.Native;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Clipboard
{
	/// <summary>
	/// Owns the X11 <c>CLIPBOARD</c> selection and acts as an XDND drag source on a private X connection, because Uno's X11 host can neither
	/// publish file lists nor start drags. Every Xlib call on the connection runs on one dedicated thread; other threads post work to it.
	/// </summary>
	/// <remarks>
	/// Implements the selection owner side of ICCCM section 2 (TARGETS, <c>INCR</c> for large lists) and the source side of XDND version 5.
	/// Reading prefers <c>x-special/gnome-copied-files</c>, then <c>text/uri-list</c> plus the KDE cut marker.
	/// </remarks>
	internal sealed unsafe class X11SelectionHost : IDisposable
	{
		private const int MaxReadBytes = 64 * 1024 * 1024;
		private const int ReadTimeoutMilliseconds = 3000;
		private const int IncrIdleTimeoutMilliseconds = 10_000;
		private const int DragTickMilliseconds = 15;
		private const int DropFinishTimeoutMilliseconds = 10_000;
		private const int DragStartGraceMilliseconds = 400;
		private const int DragMaxMilliseconds = 120_000;

		private static readonly object ErrorHandlerLock = new();
		private static readonly ConcurrentDictionary<nint, byte> OwnDisplays = new();
		private static nint s_previousErrorHandler;
		private static bool s_errorHandlerInstalled;

		private readonly nint _display;
		private readonly nuint _root;
		private readonly nuint _window;
		private readonly int _wakeRead;
		private readonly int _wakeWrite;
		private readonly int _incrThreshold;
		private readonly Thread _thread;
		private readonly ConcurrentQueue<Action> _queue = new();
		private readonly List<IncrSend> _incrSends = [];
		private readonly Dictionary<nuint, string> _offeredTargets = [];
		private volatile bool _stop;
		private bool _disposed;

		private readonly nuint _clipboard;
		private readonly nuint _targets;
		private readonly nuint _incr;
		private readonly nuint _atomType;
		private readonly nuint _cardinal;
		private readonly nuint _property;
		private readonly nuint _timeProperty;
		private readonly nuint _xdndAware;
		private readonly nuint _xdndEnter;
		private readonly nuint _xdndPosition;
		private readonly nuint _xdndStatus;
		private readonly nuint _xdndLeave;
		private readonly nuint _xdndDrop;
		private readonly nuint _xdndFinished;
		private readonly nuint _xdndSelection;
		private readonly nuint _xdndActionCopy;
		private readonly nuint _xdndActionMove;
		private readonly nuint _netWmPid;
		private readonly nuint _uriListAtom;
		private readonly nuint _utf8Atom;
		private readonly nuint _textAtom;

		private ClipboardFileList? _clipboardContent;
		private DragSession? _drag;

		/// <summary>Raised on the host thread when another application takes the clipboard over.</summary>
		public event EventHandler? ClipboardOwnerLost;

		private X11SelectionHost(nint display, nuint window, nuint root, int wakeRead, int wakeWrite, int incrThreshold)
		{
			_display = display;
			_window = window;
			_root = root;
			_wakeRead = wakeRead;
			_wakeWrite = wakeWrite;
			_incrThreshold = incrThreshold;

			_clipboard = Atom("CLIPBOARD");
			_targets = Atom("TARGETS");
			_incr = Atom("INCR");
			_atomType = Atom("ATOM");
			_cardinal = Atom("CARDINAL");
			_property = Atom("FILES_SELECTION_DATA");
			_timeProperty = Atom("FILES_XDND_TIME");
			_xdndAware = Atom("XdndAware");
			_xdndEnter = Atom("XdndEnter");
			_xdndPosition = Atom("XdndPosition");
			_xdndStatus = Atom("XdndStatus");
			_xdndLeave = Atom("XdndLeave");
			_xdndDrop = Atom("XdndDrop");
			_xdndFinished = Atom("XdndFinished");
			_xdndSelection = Atom("XdndSelection");
			_xdndActionCopy = Atom("XdndActionCopy");
			_xdndActionMove = Atom("XdndActionMove");
			_netWmPid = Atom("_NET_WM_PID");
			_uriListAtom = Atom(ClipboardFormats.UriList);
			_utf8Atom = Atom(ClipboardFormats.Utf8String);
			_textAtom = Atom(ClipboardFormats.PlainText);

			foreach (var name in new[]
			{
				ClipboardFormats.GnomeCopiedFiles, ClipboardFormats.UriList, ClipboardFormats.KdeCutSelection,
				ClipboardFormats.Utf8String, ClipboardFormats.PlainTextUtf8, ClipboardFormats.PlainText,
			})
			{
				_offeredTargets[Atom(name)] = name;
			}

			_thread = new Thread(Run) { IsBackground = true, Name = "X11 selection host" };
		}

		/// <summary>
		/// Connects to the X server. Returns <see langword="null"/> when no display is reachable.
		/// </summary>
		/// <param name="incrThreshold">The payload size above which transfers use <c>INCR</c>; defaults to what one X request can carry.</param>
		public static X11SelectionHost? TryCreate(int? incrThreshold = null)
		{
			nint display;
			try
			{
				// Safe to repeat: Uno's X11 host calls it too, and several connections may be opened from different threads
				X11Native.XInitThreads();
				display = X11Native.XOpenDisplay(0);
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				return null;
			}

			if (display == 0)
				return null;

			EnsureErrorHandler();
			OwnDisplays[display] = 0;

			var root = X11Native.XDefaultRootWindow(display);
			var window = X11Native.XCreateSimpleWindow(display, root, -10, -10, 1, 1, 0, 0, 0);
			if (window == 0)
			{
				OwnDisplays.TryRemove(display, out _);
				X11Native.XCloseDisplay(display);
				return null;
			}

			X11Native.XSelectInput(display, window, X11Native.PropertyChangeMask);

			// One X request carries at most XMaxRequestSize 4-byte units; leave room for the header
			var perRequest = (int)Math.Min(X11Native.XMaxRequestSize(display) * 4 - 1024, int.MaxValue);
			var threshold = Math.Max(1024, incrThreshold ?? perRequest);

			var fds = stackalloc int[2];
			if (X11Native.Pipe2(fds, 0x80000 /* O_CLOEXEC */ | 0x800 /* O_NONBLOCK */) != 0)
			{
				X11Native.XDestroyWindow(display, window);
				OwnDisplays.TryRemove(display, out _);
				X11Native.XCloseDisplay(display);
				return null;
			}

			var host = new X11SelectionHost(display, window, root, fds[0], fds[1], threshold);
			host._thread.Start();
			return host;
		}

		private nuint Atom(string name) => X11Native.XInternAtom(_display, name, false);

		/// <summary>
		/// Takes the clipboard and offers <paramref name="content"/>.
		/// </summary>
		public Task<bool> SetClipboardAsync(ClipboardFileList content) => Post(() =>
		{
			X11Native.XSetSelectionOwner(_display, _clipboard, _window, 0);
			X11Native.XFlush(_display);
			if (X11Native.XGetSelectionOwner(_display, _clipboard) != _window)
				return false;

			_clipboardContent = content;
			return true;
		});

		/// <summary>
		/// Releases the clipboard if it is still owned by this host.
		/// </summary>
		public Task ClearClipboardAsync() => Post(() =>
		{
			if (X11Native.XGetSelectionOwner(_display, _clipboard) == _window)
			{
				X11Native.XSetSelectionOwner(_display, _clipboard, 0, 0);
				X11Native.XFlush(_display);
			}

			_clipboardContent = null;
			return true;
		});

		/// <summary>
		/// Reads a file list from the clipboard owner, whoever it is.
		/// </summary>
		public Task<ClipboardFileList?> GetClipboardFilesAsync() => Post<ClipboardFileList?>(() =>
		{
			var owner = X11Native.XGetSelectionOwner(_display, _clipboard);
			if (owner == 0)
				return null;

			if (owner == _window)
				return _clipboardContent;

			var targetsData = ConvertSelection(_clipboard, _targets);
			if (targetsData is null)
				return null;

			var offered = new HashSet<nuint>();
			for (var i = 0; i + sizeof(nuint) <= targetsData.Length; i += sizeof(nuint))
				offered.Add((nuint)BitConverter.ToUInt64(targetsData, i));

			byte[]? Fetch(string name)
			{
				var atom = Atom(name);
				return offered.Contains(atom) ? ConvertSelection(_clipboard, atom) : null;
			}

			var gnome = Fetch(ClipboardFormats.GnomeCopiedFiles);
			if (gnome is not null && ClipboardFormats.Combine(gnome, null, null) is { } fromGnome)
				return fromGnome;

			var uriList = Fetch(ClipboardFormats.UriList);
			return uriList is null ? null : ClipboardFormats.Combine(null, uriList, Fetch(ClipboardFormats.KdeCutSelection));
		});

		/// <summary>
		/// Runs an XDND drag from the current pointer position until the primary button is released.
		/// </summary>
		public Task<FileDragOutcome> DragAsync(ClipboardFileList content, CancellationToken cancellationToken)
		{
			var tcs = new TaskCompletionSource<FileDragOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
			_queue.Enqueue(() =>
			{
				if (_drag is not null || _disposed)
				{
					tcs.TrySetResult(FileDragOutcome.None);
					return;
				}

				// KWin's XWayland drag bridge ignores an XdndSelection owner taken with CurrentTime; a real server time is required
				var timestamp = GetServerTime();
				X11Native.XSetSelectionOwner(_display, _xdndSelection, _window, timestamp);
				X11Native.XFlush(_display);
				_drag = new DragSession(content, tcs, Environment.TickCount64) { Timestamp = timestamp };
			});
			Wake();

			if (cancellationToken.CanBeCanceled)
			{
				cancellationToken.Register(() =>
				{
					_queue.Enqueue(() => EndDrag(FileDragOutcome.None, sendLeave: true));
					Wake();
				});
			}

			return tcs.Task;
		}

		/// <summary>
		/// Reads the current X server time through a zero-length property append on the own window. Returns 0 (CurrentTime) on timeout.
		/// </summary>
		private nuint GetServerTime()
		{
			X11Native.XChangeProperty(_display, _window, _timeProperty, _atomType, 32, X11Native.PropModeAppend, null, 0);
			X11Native.XFlush(_display);

			return WaitFor(ev => ev->Type == X11Native.PropertyNotify && ev->Property.Window == _window && ev->Property.Atom == _timeProperty, 500, out var found)
				? found.Property.Time
				: 0;
		}

		private Task<T> Post<T>(Func<T> work)
		{
			var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
			_queue.Enqueue(() =>
			{
				try
				{
					tcs.TrySetResult(work());
				}
				catch (Exception ex)
				{
					tcs.TrySetException(ex);
				}
			});
			Wake();
			return tcs.Task;
		}

		private void Wake()
		{
			byte b = 1;
			X11Native.Write(_wakeWrite, &b, 1);
		}

		private void Run()
		{
			var pollFds = stackalloc PollFd[2];
			var xfd = X11Native.XConnectionNumber(_display);
			var drain = stackalloc byte[64];

			while (!_stop)
			{
				try
				{
					PumpEvents();
					while (_queue.TryDequeue(out var action))
						action();

					if (_drag is not null)
						TickDrag();

					ExpireIncrSends();
				}
				catch (Exception ex)
				{
					Debug.WriteLine(ex);
					// Keep serving the selection; one bad request must not end clipboard support
				}

				pollFds[0] = new PollFd { Fd = xfd, Events = 1 /* POLLIN */ };
				pollFds[1] = new PollFd { Fd = _wakeRead, Events = 1 };
				var timeout = _drag is not null ? DragTickMilliseconds : (_incrSends.Count > 0 ? 500 : 5000);
				if (X11Native.XPending(_display) > 0)
					continue;

				X11Native.Poll(pollFds, 2, timeout);
				if ((pollFds[1].Revents & 1) != 0)
				{
					while (X11Native.Read(_wakeRead, drain, 64) > 0)
					{
					}
				}
			}
		}

		private void PumpEvents()
		{
			XEvent ev;
			while (X11Native.XPending(_display) > 0)
			{
				X11Native.XNextEvent(_display, &ev);
				Dispatch(&ev);
			}
		}

		private void Dispatch(XEvent* ev)
		{
			switch (ev->Type)
			{
				case X11Native.SelectionRequest:
					HandleSelectionRequest(ev->SelectionRequest);
					break;
				case X11Native.SelectionClear:
					HandleSelectionClear(ev->SelectionClear);
					break;
				case X11Native.PropertyNotify:
					HandleIncrProperty(ev->Property);
					break;
				case X11Native.ClientMessage:
					HandleDragMessage(ev->ClientMessage);
					break;
			}
		}

		// ---- Selection owner ------------------------------------------------------------------------------------------------------

		private void HandleSelectionClear(XSelectionClearEvent ev)
		{
			if (ev.Selection == _clipboard)
			{
				_clipboardContent = null;
				ClipboardOwnerLost?.Invoke(this, EventArgs.Empty);
			}
			else if (ev.Selection == _xdndSelection && _drag is { Dropping: false })
			{
				EndDrag(FileDragOutcome.None, sendLeave: true);
			}
		}

		private void HandleSelectionRequest(XSelectionRequestEvent req)
		{
			var content = req.Selection == _clipboard ? _clipboardContent
				: req.Selection == _xdndSelection ? _drag?.Content
				: null;

			var property = req.Property == 0 ? req.Target : req.Property;
			if (content is null)
			{
				SendSelectionNotify(req, 0);
				return;
			}

			if (req.Target == _targets)
			{
				var atoms = new List<nuint> { _targets };
				foreach (var (atom, name) in _offeredTargets)
				{
					if (ClipboardFormats.Render(name, content.Paths, content.Operation) is not null)
						atoms.Add(atom);
				}

				var array = atoms.ToArray();
				fixed (nuint* p = array)
					X11Native.XChangeProperty(_display, req.Requestor, property, _atomType, 32, X11Native.PropModeReplace, (byte*)p, array.Length);

				SendSelectionNotify(req, property);
				return;
			}

			byte[]? data = _offeredTargets.TryGetValue(req.Target, out var targetName)
				? ClipboardFormats.Render(targetName, content.Paths, content.Operation)
				: null;

			if (data is null)
			{
				SendSelectionNotify(req, 0);
				return;
			}

			// UTF8_STRING data is typed UTF8_STRING; every other offered target is typed as itself
			var type = req.Target;

			if (data.Length > _incrThreshold)
			{
				var length = (nuint)data.Length;
				X11Native.XChangeProperty(_display, req.Requestor, property, _incr, 32, X11Native.PropModeReplace, (byte*)&length, 1);
				X11Native.XSelectInput(_display, req.Requestor, X11Native.PropertyChangeMask);
				_incrSends.Add(new IncrSend(req.Requestor, property, type, data, Environment.TickCount64));
				SendSelectionNotify(req, property);
				return;
			}

			ChangeBytes(req.Requestor, property, type, data, 0, data.Length);
			SendSelectionNotify(req, property);
		}

		private void ChangeBytes(nuint window, nuint property, nuint type, byte[] data, int offset, int count)
		{
			if (count == 0)
			{
				byte dummy = 0;
				X11Native.XChangeProperty(_display, window, property, type, 8, X11Native.PropModeReplace, &dummy, 0);
				return;
			}

			fixed (byte* p = &data[offset])
				X11Native.XChangeProperty(_display, window, property, type, 8, X11Native.PropModeReplace, p, count);
		}

		private void SendSelectionNotify(XSelectionRequestEvent req, nuint property)
		{
			XEvent reply = default;
			reply.Selection = new XSelectionEvent
			{
				Type = X11Native.SelectionNotify,
				Requestor = req.Requestor,
				Selection = req.Selection,
				Target = req.Target,
				Property = property,
				Time = req.Time,
			};

			X11Native.XSendEvent(_display, req.Requestor, false, 0, &reply);
			X11Native.XFlush(_display);
		}

		private void HandleIncrProperty(XPropertyEvent ev)
		{
			if (ev.State != X11Native.PropertyDelete)
				return;

			for (var i = 0; i < _incrSends.Count; i++)
			{
				var send = _incrSends[i];
				if (send.Requestor != ev.Window || send.Property != ev.Atom)
					continue;

				var count = Math.Min(_incrThreshold, send.Data.Length - send.Offset);
				ChangeBytes(send.Requestor, send.Property, send.Type, send.Data, send.Offset, count);
				X11Native.XFlush(_display);

				if (count == 0)
				{
					// The terminating zero-length chunk has been published
					X11Native.XSelectInput(_display, send.Requestor, 0);
					_incrSends.RemoveAt(i);
				}
				else
				{
					_incrSends[i] = send with { Offset = send.Offset + count, LastActivity = Environment.TickCount64 };
				}

				return;
			}
		}

		private void ExpireIncrSends()
		{
			var now = Environment.TickCount64;
			for (var i = _incrSends.Count - 1; i >= 0; i--)
			{
				if (now - _incrSends[i].LastActivity > IncrIdleTimeoutMilliseconds)
				{
					X11Native.XSelectInput(_display, _incrSends[i].Requestor, 0);
					_incrSends.RemoveAt(i);
				}
			}
		}

		// ---- Selection requestor --------------------------------------------------------------------------------------------------

		/// <summary>
		/// Asks the owner of <paramref name="selection"/> for <paramref name="target"/> and waits for the answer, following <c>INCR</c> transfers.
		/// </summary>
		private byte[]? ConvertSelection(nuint selection, nuint target)
		{
			X11Native.XDeleteProperty(_display, _window, _property);
			X11Native.XConvertSelection(_display, selection, target, _property, _window, 0);
			X11Native.XFlush(_display);

			if (!WaitFor(ev => ev->Type == X11Native.SelectionNotify && ev->Selection.Requestor == _window && ev->Selection.Selection == selection
				&& ev->Selection.Target == target, ReadTimeoutMilliseconds, out var notify))
			{
				return null;
			}

			if (notify.Selection.Property == 0)
				return null;

			var first = ReadProperty(_property, out var type);
			if (first is null)
				return null;

			if (type != _incr)
				return first;

			// Incremental transfer: each chunk arrives as a new value of the property; an empty chunk ends it
			using var result = new System.IO.MemoryStream();
			while (true)
			{
				if (!WaitFor(ev => ev->Type == X11Native.PropertyNotify && ev->Property.Window == _window && ev->Property.Atom == _property
					&& ev->Property.State == X11Native.PropertyNewValue, ReadTimeoutMilliseconds, out _))
				{
					return null;
				}

				var chunk = ReadProperty(_property, out _);
				if (chunk is null)
					return null;

				if (chunk.Length == 0)
					return result.ToArray();

				if (result.Length + chunk.Length > MaxReadBytes)
					return null;

				result.Write(chunk);
			}
		}

		private byte[]? ReadProperty(nuint property, out nuint type)
		{
			nuint actualType;
			int format;
			nuint count;
			nuint bytesAfter;
			byte* data;

			type = 0;
			var status = X11Native.XGetWindowProperty(_display, _window, property, 0, MaxReadBytes / 4, true, 0, &actualType, &format, &count, &bytesAfter, &data);
			if (status != 0 || data is null && count != 0)
				return null;

			try
			{
				type = actualType;
				if (bytesAfter != 0 || actualType == 0)
					return actualType == _incr ? [] : null;

				// Format 32 items are C longs in client memory
				var itemSize = format switch { 8 => 1, 16 => 2, 32 => sizeof(nint), _ => 0 };
				if (itemSize == 0)
					return null;

				var bytes = new byte[checked((int)count * itemSize)];
				if (bytes.Length > 0)
					Marshal.Copy((nint)data, bytes, 0, bytes.Length);

				return bytes;
			}
			finally
			{
				if (data is not null)
					X11Native.XFree(data);
			}
		}

		/// <summary>
		/// Pumps events (dispatching the ones that do not match, so this host keeps serving its own selections) until one matches.
		/// </summary>
		private bool WaitFor(EventMatch match, int timeoutMilliseconds, out XEvent found)
		{
			var deadline = Environment.TickCount64 + timeoutMilliseconds;
			var pollFds = stackalloc PollFd[1];
			var xfd = X11Native.XConnectionNumber(_display);
			XEvent ev;

			while (true)
			{
				while (X11Native.XPending(_display) > 0)
				{
					X11Native.XNextEvent(_display, &ev);
					if (match(&ev))
					{
						found = ev;
						return true;
					}

					Dispatch(&ev);
				}

				var remaining = deadline - Environment.TickCount64;
				if (remaining <= 0 || _stop)
				{
					found = default;
					return false;
				}

				pollFds[0] = new PollFd { Fd = xfd, Events = 1 };
				X11Native.Poll(pollFds, 1, (int)Math.Min(remaining, 100));
			}
		}

		// ---- XDND source ----------------------------------------------------------------------------------------------------------

		private delegate bool EventMatch(XEvent* ev);

		private sealed class DragSession(ClipboardFileList content, TaskCompletionSource<FileDragOutcome> completion, long startTick)
		{
			public ClipboardFileList Content { get; } = content;

			public TaskCompletionSource<FileDragOutcome> Completion { get; } = completion;

			public long StartTick { get; } = startTick;

			public bool ButtonSeen { get; set; }

			public nuint Target { get; set; }

			public int Version { get; set; }

			public bool Accepted { get; set; }

			public nuint AcceptedAction { get; set; }

			public bool AwaitingStatus { get; set; }

			public int LastX { get; set; } = int.MinValue;

			public int LastY { get; set; } = int.MinValue;

			public uint LastMask { get; set; }

			public long LastPositionTick { get; set; }

			public nuint Timestamp { get; set; }

			public bool Dropping { get; set; }

			public long DropTick { get; set; }
		}

		private void TickDrag()
		{
			var drag = _drag!;
			var now = Environment.TickCount64;

			if (drag.Dropping)
			{
				if (now - drag.DropTick > DropFinishTimeoutMilliseconds)
					EndDrag(drag.AcceptedAction == _xdndActionMove ? FileDragOutcome.Move : FileDragOutcome.Copy, sendLeave: false);

				return;
			}

			if (now - drag.StartTick > DragMaxMilliseconds)
			{
				EndDrag(FileDragOutcome.None, sendLeave: true);
				return;
			}

			nuint root, child;
			int rootX, rootY, winX, winY;
			uint mask;
			X11Native.XQueryPointer(_display, _root, &root, &child, &rootX, &rootY, &winX, &winY, &mask);

			var down = (mask & X11Native.Button1Mask) != 0;
			if (!drag.ButtonSeen)
			{
				if (down)
				{
					drag.ButtonSeen = true;
				}
				else if (now - drag.StartTick > DragStartGraceMilliseconds)
				{
					EndDrag(FileDragOutcome.None, sendLeave: false);
					return;
				}
			}

			var target = FindXdndTarget(rootX, rootY, out var version);

			if (target != drag.Target)
			{
				if (drag.Target != 0)
					SendClientMessage(drag.Target, _xdndLeave, (nint)_window, 0, 0, 0, 0);

				drag.Target = target;
				drag.Version = version;
				drag.Accepted = false;
				drag.AwaitingStatus = false;
				drag.LastX = drag.LastY = int.MinValue;

				if (target != 0)
				{
					SendClientMessage(target, _xdndEnter, (nint)_window, (nint)version << 24, (nint)_uriListAtom, (nint)_textAtom, 0);
				}
			}

			if (!down && drag.ButtonSeen)
			{
				if (drag.Target != 0 && drag.Accepted)
				{
					SendClientMessage(drag.Target, _xdndDrop, (nint)_window, 0, (nint)drag.Timestamp, 0, 0);
					drag.Dropping = true;
					drag.DropTick = now;
				}
				else
				{
					EndDrag(FileDragOutcome.None, sendLeave: true);
				}

				return;
			}

			if (drag.Target != 0 && (rootX != drag.LastX || rootY != drag.LastY || mask != drag.LastMask)
				&& (!drag.AwaitingStatus || now - drag.LastPositionTick > 250))
			{
				// Shift asks for a move, everything else copies; the receiving application decides what it does with that
				var action = (mask & X11Native.ShiftMask) != 0 ? _xdndActionMove : _xdndActionCopy;
				SendClientMessage(drag.Target, _xdndPosition, (nint)_window, 0, (nint)(((long)rootX << 16) | (uint)(rootY & 0xFFFF)), (nint)drag.Timestamp, (nint)action);
				drag.LastX = rootX;
				drag.LastY = rootY;
				drag.LastMask = mask;
				drag.AwaitingStatus = true;
				drag.LastPositionTick = now;
			}
		}

		private void HandleDragMessage(XClientMessageEvent msg)
		{
			if (_drag is not { } drag)
				return;

			if (msg.MessageType == _xdndStatus && (nuint)msg.L0 == drag.Target)
			{
				drag.Accepted = (msg.L1 & 1) != 0;
				drag.AcceptedAction = (nuint)msg.L4;
				drag.AwaitingStatus = false;
			}
			else if (msg.MessageType == _xdndFinished && (nuint)msg.L0 == drag.Target && drag.Dropping)
			{
				var accepted = (msg.L1 & 1) != 0;
				var action = (nuint)msg.L2;
				EndDrag(!accepted ? FileDragOutcome.None : action == _xdndActionMove ? FileDragOutcome.Move : FileDragOutcome.Copy, sendLeave: false);
			}
		}

		private void EndDrag(FileDragOutcome outcome, bool sendLeave)
		{
			if (_drag is not { } drag)
				return;

			_drag = null;
			if (sendLeave && drag.Target != 0)
				SendClientMessage(drag.Target, _xdndLeave, (nint)_window, 0, 0, 0, 0);

			if (X11Native.XGetSelectionOwner(_display, _xdndSelection) == _window)
				X11Native.XSetSelectionOwner(_display, _xdndSelection, 0, 0);

			X11Native.XFlush(_display);
			drag.Completion.TrySetResult(outcome);
		}

		private void SendClientMessage(nuint window, nuint type, nint l0, nint l1, nint l2, nint l3, nint l4)
		{
			XEvent ev = default;
			ev.ClientMessage = new XClientMessageEvent
			{
				Type = X11Native.ClientMessage,
				Window = window,
				MessageType = type,
				Format = 32,
				L0 = l0,
				L1 = l1,
				L2 = l2,
				L3 = l3,
				L4 = l4,
			};

			X11Native.XSendEvent(_display, window, false, 0, &ev);
			X11Native.XFlush(_display);
		}

		/// <summary>
		/// Finds the deepest XDND-aware window under the pointer that does not belong to this process. Returns 0 when there is none.
		/// </summary>
		private nuint FindXdndTarget(int rootX, int rootY, out int version)
		{
			version = 0;
			nuint best = 0;
			var current = _root;

			for (var depth = 0; depth < 32; depth++)
			{
				int dx, dy;
				nuint child;
				if (!X11Native.XTranslateCoordinates(_display, _root, current, rootX, rootY, &dx, &dy, &child) || child == 0)
					break;

				current = child;
				if (ReadCardinal(current, _xdndAware, _atomType) is { } aware)
				{
					best = current;
					version = (int)Math.Min(5, aware);
				}
			}

			if (best == 0 || version < 3)
			{
				version = 0;
				return 0;
			}

			// In-app drags are Uno's business; the window of this process is never a target for the external drag
			if (ReadCardinal(best, _netWmPid, _cardinal) is { } pid && pid == (nuint)X11Native.GetPid())
			{
				version = 0;
				return 0;
			}

			return best;
		}

		private nuint? ReadCardinal(nuint window, nuint property, nuint type)
		{
			nuint actualType;
			int format;
			nuint count;
			nuint bytesAfter;
			byte* data;

			var status = X11Native.XGetWindowProperty(_display, window, property, 0, 1, false, type, &actualType, &format, &count, &bytesAfter, &data);
			if (status != 0 || data is null)
				return null;

			try
			{
				return count >= 1 && format == 32 ? *(nuint*)data : null;
			}
			finally
			{
				X11Native.XFree(data);
			}
		}

		// ---- Lifetime -------------------------------------------------------------------------------------------------------------

		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			_stop = true;
			Wake();
			if (!_thread.Join(2000))
				return; // The thread owns the connection; leaking it beats closing it under a running Xlib call

			if (_drag is { } drag)
				drag.Completion.TrySetResult(FileDragOutcome.None);

			X11Native.XDestroyWindow(_display, _window);
			OwnDisplays.TryRemove(_display, out _);
			X11Native.XCloseDisplay(_display);
			X11Native.Close(_wakeRead);
			X11Native.Close(_wakeWrite);
		}

		private static void EnsureErrorHandler()
		{
			lock (ErrorHandlerLock)
			{
				if (s_errorHandlerInstalled)
					return;

				// Errors on our connection (a requestor window vanishing mid-transfer) are expected and harmless; everything else goes to the previous handler
				s_previousErrorHandler = X11Native.XSetErrorHandler((nint)(delegate* unmanaged<nint, nint, int>)&OnXError);
				s_errorHandlerInstalled = true;
			}
		}

		[UnmanagedCallersOnly]
		private static int OnXError(nint display, nint errorEvent)
		{
			if (OwnDisplays.ContainsKey(display))
				return 0;

			var previous = s_previousErrorHandler;
			return previous == 0 ? 0 : ((delegate* unmanaged<nint, nint, int>)previous)(display, errorEvent);
		}

		private readonly record struct IncrSend(nuint Requestor, nuint Property, nuint Type, byte[] Data, long LastActivity)
		{
			public int Offset { get; init; }
		}
	}
}
