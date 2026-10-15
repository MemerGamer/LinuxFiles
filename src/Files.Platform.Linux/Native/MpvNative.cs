// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace Files.Platform.Linux.Native
{
	internal static unsafe partial class MpvNative
	{
		internal const int FormatString = 1;
		internal const int FormatFlag = 3;
		internal const int FormatDouble = 5;

		[LibraryImport("libc", EntryPoint = "setlocale", StringMarshalling = StringMarshalling.Utf8)]
		internal static partial nint SetLocale(int category, string locale);

		[LibraryImport("libmpv.so.2")]
		internal static partial nuint mpv_client_api_version();
		[LibraryImport("libmpv.so.2")]
		internal static partial nint mpv_create();
		[LibraryImport("libmpv.so.2")]
		internal static partial int mpv_initialize(nint handle);
		[LibraryImport("libmpv.so.2", StringMarshalling = StringMarshalling.Utf8)]
		internal static partial int mpv_set_option_string(nint handle, string name, string value);
		[LibraryImport("libmpv.so.2")]
		internal static partial int mpv_command(nint handle, byte** args);
		[LibraryImport("libmpv.so.2", StringMarshalling = StringMarshalling.Utf8)]
		internal static partial int mpv_set_property(nint handle, string name, int format, void* data);
		[LibraryImport("libmpv.so.2", StringMarshalling = StringMarshalling.Utf8)]
		internal static partial int mpv_get_property(nint handle, string name, int format, void* data);
		[LibraryImport("libmpv.so.2", StringMarshalling = StringMarshalling.Utf8)]
		internal static partial int mpv_observe_property(nint handle, ulong userData, string name, int format);
		[LibraryImport("libmpv.so.2")]
		internal static partial Event* mpv_wait_event(nint handle, double timeout);
		[LibraryImport("libmpv.so.2")]
		internal static partial void mpv_wakeup(nint handle);
		[LibraryImport("libmpv.so.2")]
		internal static partial void mpv_terminate_destroy(nint handle);
		[LibraryImport("libmpv.so.2")]
		internal static partial nint mpv_error_string(int error);
		[LibraryImport("libmpv.so.2")]
		internal static partial void mpv_free(nint data);
		[LibraryImport("libmpv.so.2")]
		internal static partial int mpv_render_context_create(out nint context, nint handle, RenderParam* parameters);
		[LibraryImport("libmpv.so.2")]
		internal static partial void mpv_render_context_set_update_callback(nint context, delegate* unmanaged[Cdecl]<nint, void> callback, nint userData);
		[LibraryImport("libmpv.so.2")]
		internal static partial ulong mpv_render_context_update(nint context);
		[LibraryImport("libmpv.so.2")]
		internal static partial int mpv_render_context_render(nint context, RenderParam* parameters);
		[LibraryImport("libmpv.so.2")]
		internal static partial void mpv_render_context_free(nint context);

		[StructLayout(LayoutKind.Sequential)]
		internal struct Event
		{
			internal int Id;
			internal int Error;
			internal ulong UserData;
			internal nint Data;
		}

		[StructLayout(LayoutKind.Sequential)]
		internal struct EventProperty
		{
			internal nint Name;
			internal int Format;
			internal nint Data;
		}

		[StructLayout(LayoutKind.Sequential)]
		internal struct RenderParam
		{
			internal int Type;
			internal nint Data;
			internal RenderParam(int type, nint data) => (Type, Data) = (type, data);
		}
	}
}
