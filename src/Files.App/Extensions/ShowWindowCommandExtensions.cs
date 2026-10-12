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

	}
}
