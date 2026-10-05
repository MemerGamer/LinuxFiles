// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.App.Extensions
{
	/// <summary>
	/// Converts between <see cref="ShowWindowCommand"/> and the Win32 <see cref="SHOW_WINDOW_CMD"/>.
	/// </summary>
	public static class ShowWindowCommandExtensions
	{
		/// <summary>
		/// Converts a Win32 show command to a <see cref="ShowWindowCommand"/>; unknown values map to <see cref="ShowWindowCommand.Normal"/>.
		/// </summary>
		public static ShowWindowCommand ToShowWindowCommand(this SHOW_WINDOW_CMD command)
		{
			return command switch
			{
				SHOW_WINDOW_CMD.SW_MAXIMIZE => ShowWindowCommand.Maximized,
				SHOW_WINDOW_CMD.SW_SHOWMINIMIZED or
				SHOW_WINDOW_CMD.SW_MINIMIZE or
				SHOW_WINDOW_CMD.SW_SHOWMINNOACTIVE or
				SHOW_WINDOW_CMD.SW_FORCEMINIMIZE => ShowWindowCommand.Minimized,
				_ => ShowWindowCommand.Normal,
			};
		}

		/// <summary>
		/// Converts a <see cref="ShowWindowCommand"/> to the Win32 show command stored in shortcuts.
		/// </summary>
		public static SHOW_WINDOW_CMD ToWin32(this ShowWindowCommand command)
		{
			return command switch
			{
				ShowWindowCommand.Maximized => SHOW_WINDOW_CMD.SW_MAXIMIZE,
				ShowWindowCommand.Minimized => SHOW_WINDOW_CMD.SW_SHOWMINNOACTIVE,
				_ => SHOW_WINDOW_CMD.SW_NORMAL,
			};
		}
	}
}
