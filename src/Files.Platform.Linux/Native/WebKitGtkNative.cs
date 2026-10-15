// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace Files.Platform.Linux.Native
{
	// GTK 3 / WebKitGTK 4.1 headers; gboolean and enums are 32-bit, gulong is pointer-sized.
	internal static unsafe partial class WebKitGtkNative
	{
		[LibraryImport("libgtk-3.so.0")]
		internal static partial void gtk_disable_setlocale();
		[LibraryImport("libgtk-3.so.0")]
		internal static partial int gtk_init_check(nint argc, nint argv);
		[LibraryImport("libgtk-3.so.0")]
		internal static partial nint gtk_offscreen_window_new();
		[LibraryImport("libgtk-3.so.0")]
		internal static partial void gtk_window_set_default_size(nint window, int width, int height);
		[LibraryImport("libgtk-3.so.0")]
		internal static partial void gtk_container_add(nint container, nint widget);
		[LibraryImport("libgtk-3.so.0")]
		internal static partial int gtk_widget_get_scale_factor(nint widget);
		[LibraryImport("libgtk-3.so.0")]
		internal static partial void gtk_widget_show_all(nint widget);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_web_context_set_sandbox_enabled(nint context, int enabled);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial int webkit_web_context_get_sandbox_enabled(nint context);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_web_view_set_zoom_level(nint view, double zoomLevel);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial nint webkit_settings_get_all_features();
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial nuint webkit_feature_list_get_length(nint list);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial nint webkit_feature_list_get(nint list, nuint index);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_feature_list_unref(nint list);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial nint webkit_feature_get_identifier(nint feature);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_settings_set_feature_enabled(nint settings, nint feature, int enabled);
		[LibraryImport("libgtk-3.so.0")]
		internal static partial void gtk_widget_destroy(nint widget);
		[LibraryImport("libgtk-3.so.0")]
		internal static partial void gtk_main();
		[LibraryImport("libgtk-3.so.0")]
		internal static partial void gtk_main_quit();

		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial nint webkit_website_data_manager_new_ephemeral();
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial nint webkit_web_context_new_with_website_data_manager(nint manager);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_web_context_set_cache_model(nint context, int model);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial nint webkit_web_view_new_with_context(nint context);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial nint webkit_settings_new();
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_web_view_set_settings(nint view, nint settings);
		[LibraryImport("libwebkit2gtk-4.1.so.0", StringMarshalling = StringMarshalling.Utf8)]
		internal static partial void webkit_web_view_load_html(nint view, string content, string baseUri);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_web_view_get_snapshot(nint view, int region, int options, nint cancellable, delegate* unmanaged[Cdecl]<nint, nint, nint, void> callback, nint data);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial nint webkit_web_view_get_snapshot_finish(nint view, nint result, out nint error);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_policy_decision_ignore(nint decision);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial nint webkit_navigation_policy_decision_get_navigation_action(nint decision);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial nint webkit_navigation_action_get_request(nint action);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial nint webkit_uri_request_get_uri(nint request);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_download_cancel(nint download);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_settings_set_enable_javascript(nint settings, int enabled);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_settings_set_enable_plugins(nint settings, int enabled);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_settings_set_enable_webgl(nint settings, int enabled);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_settings_set_enable_webaudio(nint settings, int enabled);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_settings_set_enable_media(nint settings, int enabled);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_settings_set_enable_media_stream(nint settings, int enabled);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_settings_set_enable_developer_extras(nint settings, int enabled);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_settings_set_enable_html5_local_storage(nint settings, int enabled);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_settings_set_enable_html5_database(nint settings, int enabled);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_settings_set_enable_offline_web_application_cache(nint settings, int enabled);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_settings_set_enable_page_cache(nint settings, int enabled);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_settings_set_enable_dns_prefetching(nint settings, int enabled);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_settings_set_enable_write_console_messages_to_stdout(nint settings, int enabled);
		[LibraryImport("libwebkit2gtk-4.1.so.0")]
		internal static partial void webkit_settings_set_hardware_acceleration_policy(nint settings, int policy);

		[LibraryImport("libgobject-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)]
		internal static partial nuint g_signal_connect_data(nint instance, string signal, nint callback, nint data, nint destroyData, int flags);
		[LibraryImport("libgobject-2.0.so.0")]
		internal static partial void g_object_unref(nint instance);

		[LibraryImport("libglib-2.0.so.0")]
		internal static partial uint g_timeout_add(uint interval, delegate* unmanaged[Cdecl]<nint, int> callback, nint data);
		[LibraryImport("libglib-2.0.so.0")]
		internal static partial int g_source_remove(uint tag);
		[LibraryImport("libglib-2.0.so.0")]
		internal static partial void g_error_free(nint error);

		[LibraryImport("libcairo.so.2")]
		internal static partial void cairo_surface_flush(nint surface);
		[LibraryImport("libcairo.so.2")]
		internal static partial void cairo_surface_destroy(nint surface);
		[LibraryImport("libcairo.so.2")]
		internal static partial int cairo_surface_status(nint surface);
		[LibraryImport("libcairo.so.2")]
		internal static partial int cairo_surface_get_type(nint surface);
		[LibraryImport("libcairo.so.2")]
		internal static partial int cairo_image_surface_get_format(nint surface);
		[LibraryImport("libcairo.so.2")]
		internal static partial int cairo_image_surface_get_width(nint surface);
		[LibraryImport("libcairo.so.2")]
		internal static partial int cairo_image_surface_get_height(nint surface);
		[LibraryImport("libcairo.so.2")]
		internal static partial int cairo_image_surface_get_stride(nint surface);
		[LibraryImport("libcairo.so.2")]
		internal static partial nint cairo_image_surface_get_data(nint surface);

	}
}
