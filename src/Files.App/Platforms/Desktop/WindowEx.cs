// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Files.App.Data.Items
{
	/// <summary>
	/// Desktop (Uno Skia) replacement for the Win32-based <c>Data/Items/WindowEx.cs</c>.
	/// Window placement persistence, min-size enforcement and the Win32 window handle are not implemented yet.
	/// </summary>
	public partial class WindowEx : Window, IDisposable
	{
		/// <summary>
		/// Gets the native window handle. Always zero on Linux; callers must not depend on it.
		/// </summary>
		public nint WindowHandle { get; }

		public int MinWidth { get; }

		public int MinHeight { get; }

		public bool IsMaximizable
		{
			get;
			set
			{
				field = value;

				if (AppWindow.Presenter is OverlappedPresenter overlapped)
					overlapped.IsMaximizable = value;
			}
		} = true;

		public bool IsMinimizable
		{
			get;
			set
			{
				field = value;

				if (AppWindow.Presenter is OverlappedPresenter overlapped)
					overlapped.IsMinimizable = value;
			}
		} = true;

		protected virtual bool PersistPlacement => false;

		public WindowEx(int minWidth = 400, int minHeight = 300)
		{
			MinWidth = minWidth;
			MinHeight = minHeight;
		}

		public void Dispose()
		{
		}
	}
}
