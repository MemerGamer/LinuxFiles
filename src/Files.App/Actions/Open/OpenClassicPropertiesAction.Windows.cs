// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace Files.App.Actions
{
	internal sealed partial class OpenClassicPropertiesAction : ObservableObject, IAction
	{
		public Task ExecuteAsync(object? parameter = null)
		{
			if (context.HasSelection && context?.SelectedItem?.ItemPath is not null)
				ExecuteShellCommand(context.SelectedItem.ItemPath);
			else if (context?.Folder?.ItemPath is not null)
				ExecuteShellCommand(context.Folder.ItemPath);

			return Task.CompletedTask;
		}

		private unsafe void ExecuteShellCommand(string itemPath)
		{
			SHELLEXECUTEINFOW info = default;
			info.cbSize = (uint)Marshal.SizeOf(info);
			info.nShow = 5; // SW_SHOW
			info.fMask = 0x0000000C; // SEE_MASK_INVOKEIDLIST

			fixed (char* cVerb = "properties", lpFile = itemPath)
			{
				info.lpVerb = cVerb;
				info.lpFile = lpFile;

				PInvoke.ShellExecuteEx(ref info);
			}
		}
	}
}
