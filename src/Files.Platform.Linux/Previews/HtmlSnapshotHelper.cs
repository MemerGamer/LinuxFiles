// Copyright (c) Files Community
// Licensed under the MIT License.

using SkiaSharp;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using static Files.Platform.Linux.Native.WebKitGtkNative;

namespace Files.Platform.Linux.Previews
{
	/// <summary>Runs only in a disposable process, before any Uno or single-instance initialization.</summary>
	public static unsafe class HtmlSnapshotHelper
	{
		internal const string Switch = "--linuxfiles-html-snapshot";
		internal const int MaxDocumentBytes = 8 * 1024 * 1024;
		internal const int MaxHeight = 8192;
		internal const int MaxWidth = 2048;
		private const int MinTrimmedHeight = 100;
		internal const string ScaleVariable = "LINUXFILES_HTML_SNAPSHOT_SCALE";

		// Resource hints that connect before (or regardless of) the Content-Security-Policy check.
		private static readonly string[] DisabledFeatures = ["LinkPreconnect", "LinkPreconnectEarlyHints", "LinkPrefetch", "SpeculationRulesPrefetch"];

		// The policy precedes all untrusted markup. Additional policies can only restrict it further.
		// Keeping the original file URI as the base preserves relative local images and stylesheets.
		internal const string DocumentPrefix = "<!doctype html><meta charset=\"utf-8\">" +
			"<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; " +
			"img-src file: data:; style-src file: data: 'unsafe-inline'; font-src file: data:; " +
			"script-src 'none'; connect-src 'none'; frame-src 'none'; object-src 'none'; " +
			"media-src 'none'; base-uri 'none'; form-action 'none'\">" +
			"<meta http-equiv=\"x-dns-prefetch-control\" content=\"off\">";

		// All callbacks execute on the helper's GTK main thread; one document per process.
		private static int resultCode = 1;
		private static int maxHeight;
		private static int pixelWidth;
		private static string initialUri = "";
		private static bool initialNavigation;
		private static bool snapshotStarted;
		private static bool watchdogExpired;

		public static bool TryRunHelper(string[] args, out int exitCode)
		{
			exitCode = 1;
			if (args.Length == 0 || args[0] != Switch) return false;
			if (!TryParseArguments(args, out var path, out var width, out var height))
			{
				Console.Error.WriteLine("[Files HTML] Invalid snapshot arguments.");
				return true;
			}
			try { exitCode = Render(path, width, height); }
			catch (Exception ex) { Console.Error.WriteLine($"[Files HTML] {ex.Message}"); }
			return true;
		}

		internal static bool TryParseArguments(string[] args, out string path, out int width, out int height)
		{
			path = "";
			width = height = 0;
			if (args.Length != 4 || args[0] != Switch || !Path.IsPathFullyQualified(args[1]) || args[1].Contains('\0')) return false;
			if (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out width) || width is < 1 or > MaxWidth ||
				!int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out height) || height is < 1 or > MaxHeight) return false;
			path = args[1];
			return true;
		}

		private static int Render(string path, int width, int height)
		{
			// Pin a regular inode and bound the actual read, including a file growing during the read.
			using var source = PreviewFile.OpenRead(path);
			if (source.Length > MaxDocumentBytes) throw new IOException("HTML document exceeds 8 MiB.");
			using var bounded = new PreviewReadStream(source, MaxDocumentBytes);
			using var reader = new StreamReader(bounded, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
			var html = DocumentPrefix + reader.ReadToEnd();
			initialUri = new Uri(path).AbsoluteUri;
			maxHeight = height;
			resultCode = 1;
			initialNavigation = snapshotStarted = watchdogExpired = false;

			// The web process must run in WebKit's bubblewrap sandbox; without bwrap the source fallback is shown instead.
			if (!IsOnPath("bwrap") || !IsOnPath("xdg-dbus-proxy")) throw new IOException("WebKit sandbox tools (bwrap, xdg-dbus-proxy) are unavailable.");
			gtk_disable_setlocale();
			if (gtk_init_check(0, 0) == 0) throw new IOException("No usable GTK display.");
			var manager = webkit_website_data_manager_new_ephemeral();
			var context = webkit_web_context_new_with_website_data_manager(manager);
			var settings = webkit_settings_new();
			var window = gtk_offscreen_window_new();
			uint watchdog = 0;
			try
			{
				webkit_web_context_set_cache_model(context, 0); // DOCUMENT_VIEWER, no resource cache.
				// Must precede the first web view of the context; no extra sandbox paths are granted.
				webkit_web_context_set_sandbox_enabled(context, 1);
				if (webkit_web_context_get_sandbox_enabled(context) == 0) throw new IOException("WebKit sandbox could not be enabled.");
				ConfigureSettings(settings);
				var view = webkit_web_view_new_with_context(context);
				webkit_web_view_set_settings(view, settings);
				// Page zoom renders device pixels on every GDK backend (GDK_SCALE only affects X11) while keeping the CSS width.
				var scale = int.TryParse(Environment.GetEnvironmentVariable(ScaleVariable), NumberStyles.None, CultureInfo.InvariantCulture, out var requested) ? Math.Clamp(requested, 1, 2) : 1;
				webkit_web_view_set_zoom_level(view, scale);
				// The visible region bounds WebKit's snapshot allocation; trailing background rows are trimmed afterwards.
				gtk_window_set_default_size(window, width * scale, height);
				gtk_container_add(window, view);
				g_signal_connect_data(view, "load-changed", (nint)(delegate* unmanaged[Cdecl]<nint, int, nint, void>)&LoadChanged, 0, 0, 0);
				g_signal_connect_data(view, "load-failed", (nint)(delegate* unmanaged[Cdecl]<nint, int, nint, nint, nint, int>)&LoadFailed, 0, 0, 0);
				g_signal_connect_data(view, "decide-policy", (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, nint, int>)&DecidePolicy, 0, 0, 0);
				g_signal_connect_data(view, "web-process-terminated", (nint)(delegate* unmanaged[Cdecl]<nint, int, nint, void>)&ProcessTerminated, 0, 0, 0);
				g_signal_connect_data(view, "create", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint>)&CreateWindow, 0, 0, 0);
				g_signal_connect_data(context, "download-started", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&DownloadStarted, 0, 0, 0);
				gtk_widget_show_all(window);
				pixelWidth = checked(width * scale * gtk_widget_get_scale_factor(view));
				watchdog = g_timeout_add(10_000, &Watchdog, 0);
				webkit_web_view_load_html(view, html, initialUri);
				gtk_main();
				if (resultCode != 0) Console.Error.WriteLine("[Files HTML] Rendering failed or timed out.");
				return resultCode;
			}
			finally
			{
				if (watchdog != 0 && !watchdogExpired) g_source_remove(watchdog);
				gtk_widget_destroy(window);
				g_object_unref(settings);
				g_object_unref(context);
				g_object_unref(manager);
			}
		}

		private static void ConfigureSettings(nint settings)
		{
			webkit_settings_set_enable_javascript(settings, 0);
			webkit_settings_set_enable_plugins(settings, 0);
			webkit_settings_set_enable_webgl(settings, 0);
			webkit_settings_set_enable_webaudio(settings, 0);
			webkit_settings_set_enable_media(settings, 0);
			webkit_settings_set_enable_media_stream(settings, 0);
			webkit_settings_set_enable_developer_extras(settings, 0);
			webkit_settings_set_enable_html5_local_storage(settings, 0);
			webkit_settings_set_enable_html5_database(settings, 0);
			webkit_settings_set_enable_offline_web_application_cache(settings, 0);
			webkit_settings_set_enable_page_cache(settings, 0);
			webkit_settings_set_enable_dns_prefetching(settings, 0);
			webkit_settings_set_enable_write_console_messages_to_stdout(settings, 0);
			webkit_settings_set_hardware_acceleration_policy(settings, 2);
			DisableFeatures(settings);
		}

		// Fails closed: WebKitGTK without the feature API (before 2.42) or without these features is not used.
		private static void DisableFeatures(nint settings)
		{
			var features = webkit_settings_get_all_features();
			if (features == 0) throw new IOException("WebKit feature list unavailable.");
			try
			{
				var preconnectDisabled = false;
				var count = webkit_feature_list_get_length(features);
				for (nuint i = 0; i < count; i++)
				{
					var feature = webkit_feature_list_get(features, i);
					var identifier = Marshal.PtrToStringUTF8(webkit_feature_get_identifier(feature));
					if (Array.IndexOf(DisabledFeatures, identifier) < 0) continue;
					webkit_settings_set_feature_enabled(settings, feature, 0);
					preconnectDisabled |= identifier == "LinkPreconnect";
				}
				if (!preconnectDisabled) throw new IOException("WebKit preconnect cannot be disabled.");
			}
			finally { webkit_feature_list_unref(features); }
		}

		// Drops trailing rows identical to the last one (page background below short documents), keeping at least minHeight rows.
		internal static int TrimUniformBottom(ReadOnlySpan<byte> pixels, int stride, int rowBytes, int height, int minHeight = 1)
		{
			var last = pixels.Slice((height - 1) * stride, rowBytes);
			for (var x = 4; x < rowBytes; x += 4)
				if (!last.Slice(x, 4).SequenceEqual(last[..4])) return height;
			while (height > Math.Max(1, minHeight) && pixels.Slice((height - 2) * stride, rowBytes).SequenceEqual(last)) height--;
			return height;
		}

		private static bool IsOnPath(string name)
		{
			foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
				if (Path.IsPathFullyQualified(directory) && File.Exists(Path.Combine(directory, name))) return true;
			return false;
		}

		[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
		private static void LoadChanged(nint view, int loadEvent, nint data)
		{
			if (loadEvent == 0) initialNavigation = true;
			if (loadEvent != 3 || snapshotStarted) return; // WEBKIT_LOAD_FINISHED
			snapshotStarted = true;
			webkit_web_view_get_snapshot(view, 0, 0, 0, &SnapshotReady, 0); // VISIBLE
		}

		[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
		private static int DecidePolicy(nint view, nint decision, int type, nint data)
		{
			if (type == 2) return 0; // Resource responses; normal rendering, never request a download.
			if (type == 0 && !initialNavigation)
			{
				var action = webkit_navigation_policy_decision_get_navigation_action(decision);
				var request = webkit_navigation_action_get_request(action);
				if (Marshal.PtrToStringUTF8(webkit_uri_request_get_uri(request)) == initialUri)
				{
					initialNavigation = true;
					return 0;
				}
			}
			webkit_policy_decision_ignore(decision);
			return 1;
		}

		[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
		private static nint CreateWindow(nint view, nint action, nint data) => 0;

		[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
		private static void DownloadStarted(nint context, nint download, nint data) => webkit_download_cancel(download);

		[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
		private static int LoadFailed(nint view, int loadEvent, nint uri, nint error, nint data)
		{
			gtk_main_quit();
			return 1;
		}

		[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
		private static void ProcessTerminated(nint view, int reason, nint data) => gtk_main_quit();

		[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
		private static int Watchdog(nint data)
		{
			watchdogExpired = true;
			gtk_main_quit();
			return 0;
		}

		[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
		private static void SnapshotReady(nint view, nint result, nint data)
		{
			nint surface = 0, error = 0;
			try
			{
				surface = webkit_web_view_get_snapshot_finish(view, result, out error);
				if (surface == 0 || error != 0 || cairo_surface_status(surface) != 0 || cairo_surface_get_type(surface) != 0)
					throw new IOException("No image snapshot.");
				cairo_surface_flush(surface);
				var format = cairo_image_surface_get_format(surface);
				var surfaceWidth = cairo_image_surface_get_width(surface);
				var width = pixelWidth;
				var height = Math.Min(cairo_image_surface_get_height(surface), maxHeight);
				var stride = cairo_image_surface_get_stride(surface);
				if (format is not (0 or 1) || width is < 1 or > MaxWidth * 2 || height < 1 || surfaceWidth < width || stride < width * 4 || !BitConverter.IsLittleEndian)
					throw new IOException("Unsupported snapshot format or dimensions.");
				height = TrimUniformBottom(new ReadOnlySpan<byte>((void*)cairo_image_surface_get_data(surface), checked(stride * height)), stride, width * 4, height, MinTrimmedHeight);
				// Cairo ARGB32 is native-endian premultiplied BGRA; RGB24 has an unused alpha byte.
				using var pixels = new SKPixmap(new SKImageInfo(width, height, SKColorType.Bgra8888,
					format == 0 ? SKAlphaType.Premul : SKAlphaType.Opaque), cairo_image_surface_get_data(surface), stride);
				using var png = pixels.Encode(SKEncodedImageFormat.Png, 100);
				if (png is null || png.Size > HtmlSnapshotRenderer.MaxPngBytes) throw new IOException("Snapshot exceeds output limit.");
				using var stdout = Console.OpenStandardOutput();
				png.SaveTo(stdout);
				resultCode = 0;
			}
			catch (Exception ex) { Console.Error.WriteLine($"[Files HTML] {ex.Message}"); }
			finally
			{
				if (error != 0) g_error_free(error);
				if (surface != 0) cairo_surface_destroy(surface);
				gtk_main_quit();
			}
		}
	}
}
