// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Native;
using Files.Platform.Linux.Previews;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Media
{
	/// <summary>A frame owns its immutable BGRA pixels; it remains valid after the callback and player disposal.</summary>
	public sealed class MpvFrame
	{
		public int Width { get; }
		public int Height { get; }
		public int Stride { get; }
		public ReadOnlyMemory<byte> Pixels { get; }

		internal MpvFrame(int width, int height, byte[] pixels)
		{
			Width = width;
			Height = height;
			Stride = width * 4;
			Pixels = pixels;
		}
	}

	/// <summary>
	/// Optional in-process software playback. Open/commands may block and should be called off the UI thread.
	/// Events run on background threads. Dispose schedules teardown; Disposal completes after native resources are freed.
	/// </summary>
	public sealed unsafe class MpvMediaPlayer : IDisposable
	{
		private static readonly Lazy<bool> available = new(ProbeAvailability);
		private static readonly object localeGate = new();

		// Fail closed if any option is unavailable: never silently omit a security restriction.
		internal static ReadOnlySpan<(string Name, string Value)> Options => new (string, string)[]
		{
			("vo", "libmpv"), ("config", "no"), ("load-scripts", "no"), ("ytdl", "no"),
			("osc", "no"), ("input-default-bindings", "no"), ("input-vo-keyboard", "no"),
			("terminal", "no"), ("hwdec", "no"), ("keep-open", "yes"), ("pause", "yes"),
			("idle", "yes"), ("audio-display", "embedded-first"), ("access-references", "no"),
			("load-unsafe-playlists", "no"), ("autoload-files", "no"), ("sub-auto", "no"),
			("audio-file-auto", "no"), ("cover-art-auto", "no"), ("resume-playback", "no"),
			("save-position-on-quit", "no"), ("autocreate-playlist", "no"), ("prefetch-playlist", "no"),
			("ordered-chapters", "no"),
			// Without an audio device, audio-only files would otherwise fail to load.
			("audio-fallback-to-null", "yes"),
			// Bypass mpv's playlist/EDL/directory demuxers. Restrict nested libavformat IO as well.
			("demuxer", "lavf"),
			// Only the media containers used by the pane; exclude HLS, DASH, concat and other reference formats.
			("demuxer-lavf-o", "protocol_whitelist=[file],format_whitelist=[mov,mp4,m4a,3gp,3g2,mj2,matroska,webm,avi,asf,mp3,aac,flac,ogg,wav]"),
			("stream-lavf-o", "protocol_whitelist=[file]")
		};

		private readonly object nativeGate = new();
		private readonly object stateGate = new();
		private readonly AutoResetEvent renderSignal = new(false);
		private readonly TaskCompletionSource disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private nint handle;
		private nint renderContext;
		private GCHandle callbackHandle;
		private Thread? eventThread;
		private Thread? renderThread;
		private int disposed;
		private int validated;
		private int redraw;
		private int targetWidth = 640;
		private int targetHeight = 360;
		private string? requestedPath;
		private FileStream? pinnedFile;
		private bool playRequested;
		private double volume = 1;
		private double position;
		private double duration;
		private bool paused = true;
		private bool ended;
		private int videoWidth;
		private int videoHeight;
		private string? error;

		public static bool IsAvailable => available.Value;
		public Task Disposal => disposal.Task;
		public event EventHandler? StateChanged;
		/// <summary>Runs on the render thread; handlers must not call synchronous playback commands.</summary>
		public event Action<MpvFrame>? FrameRendered;
		public TimeSpan Position { get { lock (stateGate) return ToTime(position); } }
		public TimeSpan Duration { get { lock (stateGate) return ToTime(duration); } }
		public bool IsPaused { get { lock (stateGate) return paused; } }
		public bool IsEnded { get { lock (stateGate) return ended; } }
		public int VideoWidth { get { lock (stateGate) return videoWidth; } }
		public int VideoHeight { get { lock (stateGate) return videoHeight; } }
		public bool HasVideo { get { lock (stateGate) return videoWidth > 0 && videoHeight > 0; } }
		/// <summary>Diagnostic only; UI consumers should show a localized playback failure message.</summary>
		public string? Error { get { lock (stateGate) return error; } }

		public double Volume
		{
			get { lock (nativeGate) return volume; }
			set
			{
				lock (nativeGate)
				{
					ThrowIfDisposed();
					volume = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
					if (handle != 0) SetDouble("volume", volume * 100);
				}
			}
		}

		public void Open(string absolutePath)
		{
			if (!Path.IsPathFullyQualified(absolutePath) || absolutePath.Contains('\0'))
				throw new ArgumentException("An absolute local path is required.", nameof(absolutePath));
			lock (nativeGate)
			{
				ThrowIfDisposed();
				if (requestedPath is not null) throw new InvalidOperationException("Use a new player for each file.");
				requestedPath = Path.GetFullPath(absolutePath);
				try
				{
					// Only regular files; mpv reads the inode checked here even if the path is swapped afterwards.
					pinnedFile = PreviewFile.OpenRead(requestedPath);
					requestedPath = FormattableString.Invariant($"/proc/self/fd/{pinnedFile.SafeFileHandle.DangerousGetHandle()}");
					Initialize();
					ThrowIfDisposed();
					Command("loadfile", requestedPath, "replace");
				}
				catch (Exception ex)
				{
					Fail(ex.Message);
					Dispose();
					throw;
				}
			}
		}

		public void Play()
		{
			lock (nativeGate)
			{
				ThrowIfDisposed();
				playRequested = true;
				if (Volatile.Read(ref validated) == 0) return;
				if (IsEnded) Command("seek", "0", "absolute");
				SetFlag("pause", false);
			}
		}

		public void Pause()
		{
			lock (nativeGate)
			{
				ThrowIfDisposed();
				playRequested = false;
				if (handle != 0) SetFlag("pause", true);
			}
		}

		public void TogglePause()
		{
			lock (nativeGate)
			{
				if (Volatile.Read(ref validated) == 0 ? playRequested : !IsPaused) Pause();
				else Play();
			}
		}

		public void Seek(TimeSpan time)
		{
			lock (nativeGate)
			{
				ThrowIfDisposed();
				if (Volatile.Read(ref validated) != 0)
					Command("seek", Math.Max(0, time.TotalSeconds).ToString("R", CultureInfo.InvariantCulture), "absolute+keyframes");
			}
		}

		public void SetTargetSize(int widthPx, int heightPx)
		{
			lock (stateGate)
			{
				if (Volatile.Read(ref disposed) != 0) return;
				targetWidth = Math.Clamp(widthPx, 1, 1920);
				targetHeight = Math.Clamp(heightPx, 1, 1080);
				Interlocked.Exchange(ref redraw, 1);
				// The signal is disposed under this lock after both worker threads have exited.
				renderSignal.Set();
			}
		}

		internal static (int Width, int Height) FitSize(int width, int height, int videoWidth, int videoHeight)
		{
			width = Math.Clamp(width, 1, 1920);
			height = Math.Clamp(height, 1, 1080);
			if (videoWidth <= 0 || videoHeight <= 0) return (width, height);
			var scale = Math.Min((double)width / videoWidth, (double)height / videoHeight);
			return (Math.Clamp((int)Math.Round(videoWidth * scale), 1, width),
				Math.Clamp((int)Math.Round(videoHeight * scale), 1, height));
		}

		private static bool ProbeAvailability()
		{
			try
			{
				if (!NativeLibrary.TryLoad("libmpv.so.2", out var library)) return false;
				try { return (MpvNative.mpv_client_api_version() >> 16) == 2; }
				finally { NativeLibrary.Free(library); }
			}
			catch { return false; }
		}

		private void Initialize()
		{
			if (!IsAvailable) throw new NotSupportedException("libmpv client API 2 is unavailable.");
			lock (localeGate)
			{
				// LC_NUMERIC is process-wide. mpv requires C for its entire lifetime.
				if (MpvNative.SetLocale(1, "C") == 0) throw new InvalidOperationException("Cannot set LC_NUMERIC.");
				handle = MpvNative.mpv_create();
			}
			if (handle == 0) throw new InvalidOperationException("Cannot create mpv.");
			foreach (var (name, value) in Options) Check(MpvNative.mpv_set_option_string(handle, name, value));
			Check(MpvNative.mpv_initialize(handle));
			SetDouble("volume", volume * 100);
			string[] properties = ["time-pos", "duration", "pause", "eof-reached", "dwidth", "dheight"];
			for (var i = 0; i < properties.Length; i++)
				Check(MpvNative.mpv_observe_property(handle, (ulong)i + 1, properties[i],
					i is 2 or 3 ? MpvNative.FormatFlag : MpvNative.FormatDouble));

			// Create before loadfile; subsequent render calls and free belong exclusively to the render thread.
			fixed (byte* api = "sw\0"u8)
			{
				MpvNative.RenderParam* parameters = stackalloc MpvNative.RenderParam[] { new(1, (nint)api), new(0, 0) };
				Check(MpvNative.mpv_render_context_create(out renderContext, handle, parameters));
			}
			callbackHandle = GCHandle.Alloc(this);
			MpvNative.mpv_render_context_set_update_callback(renderContext, &RenderUpdate, GCHandle.ToIntPtr(callbackHandle));
			renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "Media preview render" };
			eventThread = new Thread(EventLoop) { IsBackground = true, Name = "Media preview events" };
			renderThread.Start();
			eventThread.Start();
		}

		[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
		private static void RenderUpdate(nint context)
		{
			// Native teardown quiesces callbacks before the handle and signal are freed.
			var player = (MpvMediaPlayer)GCHandle.FromIntPtr(context).Target!;
			if (Volatile.Read(ref player.disposed) == 0) player.renderSignal.Set();
		}

		private void EventLoop()
		{
			try
			{
				while (Volatile.Read(ref disposed) == 0)
				{
					var evt = MpvNative.mpv_wait_event(handle, -1);
					if (Volatile.Read(ref disposed) != 0) break;
					switch (evt->Id)
					{
						case 8: // MPV_EVENT_FILE_LOADED
							ValidateLoadedFile();
							break;
						case 7: // MPV_EVENT_END_FILE: first fields are reason and error.
							if (evt->Data != 0 && (*(int*)evt->Data is 4 or 5))
								RejectFile("Media load failed or redirected to a playlist.");
							break;
						case 22: // MPV_EVENT_PROPERTY_CHANGE
							UpdateProperty(evt->UserData, (MpvNative.EventProperty*)evt->Data);
							break;
						case 1: // MPV_EVENT_SHUTDOWN
							return;
					}
				}
			}
			catch (Exception ex)
			{
				Fail(ex.Message);
				Dispose();
			}
		}

		private void ValidateLoadedFile()
		{
			lock (nativeGate)
			{
				if (Volatile.Read(ref disposed) != 0) return;
				double count = 0;
				nint path = 0;
				try
				{
					Check(MpvNative.mpv_get_property(handle, "playlist-count", MpvNative.FormatDouble, &count));
					Check(MpvNative.mpv_get_property(handle, "path", MpvNative.FormatString, &path));
					if (count != 1 || !string.Equals(Marshal.PtrToStringUTF8(path), requestedPath, StringComparison.Ordinal))
					{
						RejectFile("Media load expanded a playlist or changed the requested path.");
						return;
					}
					Volatile.Write(ref validated, 1);
					Interlocked.Exchange(ref redraw, 1);
					renderSignal.Set();
					if (playRequested) SetFlag("pause", false);
				}
				finally { if (path != 0) MpvNative.mpv_free(path); }
			}
		}

		private void UpdateProperty(ulong id, MpvNative.EventProperty* property)
		{
			if (property == null || property->Data == 0) return;
			lock (stateGate)
			{
				var value = property->Format == MpvNative.FormatDouble ? *(double*)property->Data : 0;
				if (!double.IsFinite(value)) value = 0;
				var flag = property->Format == MpvNative.FormatFlag && *(int*)property->Data != 0;
				switch (id)
				{
					case 1: position = value; break;
					case 2: duration = value; break;
					case 3: paused = flag; break;
					case 4: ended = flag; break;
					case 5: videoWidth = (int)Math.Clamp(value, 0, int.MaxValue); break;
					case 6: videoHeight = (int)Math.Clamp(value, 0, int.MaxValue); break;
				}
			}
			if (id is 5 or 6)
			{
				Interlocked.Exchange(ref redraw, 1);
				renderSignal.Set();
			}
			NotifyState();
		}

		private void RenderLoop()
		{
			try
			{
				while (Volatile.Read(ref disposed) == 0)
				{
					renderSignal.WaitOne();
					if (Volatile.Read(ref disposed) != 0) break;
					var flags = MpvNative.mpv_render_context_update(renderContext);
					var force = Interlocked.Exchange(ref redraw, 0) != 0;
					if ((flags & 1) == 0 && !force) continue;
					// Service requested frames even before FILE_LOADED validation, so the core can progress.
					// Publishing waits for validation and display dimensions in RenderFrame.
					if ((flags & 1) == 0 && !HasVideo) continue;
					int width, height;
					lock (stateGate) (width, height) = FitSize(targetWidth, targetHeight, videoWidth, videoHeight);
					RenderFrame(width, height);
				}
			}
			catch (Exception ex)
			{
				Fail(ex.Message);
				Dispose();
			}
			finally
			{
				MpvNative.mpv_render_context_set_update_callback(renderContext, null, 0);
				MpvNative.mpv_render_context_free(renderContext);
				renderContext = 0;
			}
		}

		private void RenderFrame(int width, int height)
		{
			var length = checked(width * height * 4);
			var buffer = NativeMemory.AlignedAlloc((nuint)((length + 63) & ~63), 64);
			if (buffer == null) throw new OutOfMemoryException();
			NativeMemory.Clear(buffer, (nuint)length);
			try
			{
				int* size = stackalloc int[] { width, height };
				nuint stride = (nuint)width * 4;
				fixed (byte* format = "bgr0\0"u8)
				{
					MpvNative.RenderParam* parameters = stackalloc MpvNative.RenderParam[]
					{
						new(17, (nint)size), new(18, (nint)format), new(19, (nint)(&stride)),
						new(20, (nint)buffer), new(0, 0)
					};
					Check(MpvNative.mpv_render_context_render(renderContext, parameters));
				}
				if (Volatile.Read(ref disposed) != 0 || Volatile.Read(ref validated) == 0 || !HasVideo || Error is not null) return;
				var pixels = new byte[length];
				Marshal.Copy((nint)buffer, pixels, 0, length);
				for (var i = 3; i < length; i += 4) pixels[i] = 0xFF;
				FrameRendered?.Invoke(new MpvFrame(width, height, pixels));
			}
			finally { NativeMemory.AlignedFree(buffer); }
		}

		private void RejectFile(string message)
		{
			lock (nativeGate)
			{
				Volatile.Write(ref validated, 0);
				if (Volatile.Read(ref disposed) == 0) Command("stop");
			}
			Fail(message);
		}

		private void Fail(string message)
		{
			lock (stateGate) { error = message; paused = true; }
			NotifyState();
		}

		private void NotifyState()
		{
			if (Volatile.Read(ref disposed) == 0) StateChanged?.Invoke(this, EventArgs.Empty);
		}

		private static TimeSpan ToTime(double seconds) => TimeSpan.FromSeconds(Math.Clamp(seconds, 0, TimeSpan.MaxValue.TotalSeconds - 1));
		private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		private static void Check(int result)
		{
			if (result < 0) throw new InvalidOperationException(Marshal.PtrToStringUTF8(MpvNative.mpv_error_string(result)));
		}
		private void SetFlag(string name, bool value)
		{
			int flag = value ? 1 : 0;
			Check(MpvNative.mpv_set_property(handle, name, MpvNative.FormatFlag, &flag));
		}
		private void SetDouble(string name, double value) => Check(MpvNative.mpv_set_property(handle, name, MpvNative.FormatDouble, &value));

		private void Command(params string[] args)
		{
			byte** pointers = stackalloc byte*[args.Length + 1];
			new Span<nint>(pointers, args.Length + 1).Clear();
			try
			{
				for (var i = 0; i < args.Length; i++) pointers[i] = (byte*)Marshal.StringToCoTaskMemUTF8(args[i]);
				Check(MpvNative.mpv_command(handle, pointers));
			}
			finally
			{
				for (var i = 0; i < args.Length; i++) Marshal.FreeCoTaskMem((nint)pointers[i]);
			}
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) != 0) return;
			_ = Task.Run(Teardown);
		}

		private void Teardown()
		{
			try
			{
				lock (nativeGate)
				{
					renderSignal.Set();
					if (handle != 0) MpvNative.mpv_wakeup(handle);
				}
				// No nativeGate while joining: the event thread may still be finishing a command.
				if (renderThread?.IsAlive == true) renderThread.Join();
				if (eventThread?.IsAlive == true) eventThread.Join();
				lock (nativeGate)
				{
					// Also handles failure before the render thread started.
					if (renderContext != 0) MpvNative.mpv_render_context_free(renderContext);
					if (handle != 0) MpvNative.mpv_terminate_destroy(handle);
					renderContext = handle = 0;
					if (callbackHandle.IsAllocated) callbackHandle.Free();
					pinnedFile?.Dispose();
					pinnedFile = null;
				}
				lock (stateGate) renderSignal.Dispose();
				disposal.TrySetResult();
			}
			catch (Exception ex) { disposal.TrySetException(ex); }
		}
	}
}
