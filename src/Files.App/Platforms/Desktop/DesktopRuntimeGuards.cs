// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Files.App
{
	/// <summary>
	/// Guards against recoverable failures raised inside the Uno desktop runtime that Files' crash handler would otherwise turn into an exit.
	/// </summary>
	internal static class DesktopRuntimeGuards
	{
		/// <summary>
		/// Determines whether <paramref name="ex"/> is Uno's X11 host refusing to start a native (cross-window) drag.
		/// </summary>
		/// <remarks>
		/// Uno's X11 host throws <see cref="NotImplementedException"/> from <c>X11DragDropExtension.StartNativeDrag</c> when a drag leaves
		/// the window, and raises it through <c>Application.UnhandledException</c> as a recoverable error. Files' own drag source
		/// (<see cref="Services.Desktop.DesktopFileDragHelper"/>) takes over outside the window, so the failure is safe to ignore.
		/// </remarks>
		public static bool IsUnsupportedNativeDrag(Exception? ex)
		{
			for (var current = ex; current is not null; current = current.InnerException)
			{
				if (current is NotImplementedException &&
					current.StackTrace?.Contains("StartNativeDrag", StringComparison.Ordinal) is true)
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Marks recoverable runtime failures as handled. Returns <see langword="true"/> when the caller must not treat the exception as fatal.
		/// </summary>
		public static bool TryHandleRecoverable(Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
		{
			if (!IsUnsupportedNativeDrag(e.Exception))
				return false;

			e.Handled = true;
			App.Logger?.LogDebug("Ignored the native drag request of the Uno X11 host (outbound drag is handled by Files).");
			return true;
		}
	}
}
