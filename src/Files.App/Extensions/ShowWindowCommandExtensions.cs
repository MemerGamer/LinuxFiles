// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Extensions
{
	/// <summary>
	/// Converts between <see cref="ShowWindowCommand"/> and the Win32 SHOW_WINDOW_CMD values (as ints, so no Win32 types are needed).
	/// </summary>
	public static class ShowWindowCommandExtensions
	{
		/// <summary>
		/// Converts a Win32 show command (SHOW_WINDOW_CMD) to a <see cref="ShowWindowCommand"/>; unknown values map to <see cref="ShowWindowCommand.Normal"/>.
		/// </summary>
		public static ShowWindowCommand ToShowWindowCommand(this int command)
		{
			return command switch
			{
				3 => ShowWindowCommand.Maximized,
				2 or
				6 or
				7 or
				11 => ShowWindowCommand.Minimized,
				_ => ShowWindowCommand.Normal,
			};
		}

		/// <summary>
		/// Converts a <see cref="ShowWindowCommand"/> to the Win32 show command value stored in shortcuts;
		/// <see cref="ShowWindowCommand.Default"/> maps to SW_NORMAL (1).
		/// </summary>
		public static int ToWin32(this ShowWindowCommand command)
		{
			return command switch
			{
				ShowWindowCommand.Maximized => 3,
				ShowWindowCommand.Minimized => 7,
				_ => 1,
			};
		}
	}
}
