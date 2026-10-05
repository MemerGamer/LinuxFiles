// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Data.Enums
{
	/// <summary>
	/// Defines how a launched application's window is shown, as stored in shortcuts.
	/// </summary>
	/// <remarks>Values match the Win32 show commands that shortcut files store.</remarks>
	public enum ShowWindowCommand
	{
		/// <summary>
		/// No preference was stored; the window is shown as <see cref="Normal"/>.
		/// </summary>
		Default = 0,

		/// <summary>
		/// The window is shown at its normal size and position.
		/// </summary>
		Normal = 1,

		/// <summary>
		/// The window is shown maximized.
		/// </summary>
		Maximized = 3,

		/// <summary>
		/// The window is shown minimized without being activated.
		/// </summary>
		Minimized = 7,
	}
}
