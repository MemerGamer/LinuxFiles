// Copyright (c) Files Community
// Licensed under the MIT License.

using Uno.UI.NativeElementHosting;

namespace Files.App.Helpers
{
	internal static class NativePreviewSupport
	{
		// Inspect the running host, not the session variables: auto can fall back to X11 on a Wayland desktop.
		public static bool IsAvailable => Uno.UI.Xaml.WindowHelper.GetNativeWindow(MainWindow.Instance) is X11NativeWindow;
	}
}
