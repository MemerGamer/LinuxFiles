// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace Files.App.ViewModels.Dialogs
{
	public sealed partial class CreateShortcutDialogViewModel : ObservableObject
	{
		private unsafe Task SelectDestination()
		{
			BROWSEINFOW bi = new()
			{
				ulFlags = 0x00004000
			};

			Span<char> displayName = stackalloc char[260];
			fixed (char* displayNameBuffer = displayName)
			fixed (char* title = "Select a folder")
			{
				bi.pszDisplayName = displayNameBuffer;
				bi.lpszTitle = title;
				var pidl = PInvoke.SHBrowseForFolder(in bi);
				if (pidl is not null)
				{
					Span<char> path = stackalloc char[260];
					fixed (char* pathBuffer = path)
					{
						if (PInvoke.SHGetPathFromIDList(pidl, pathBuffer))
						{
							var length = path.IndexOf('\0');
							ShortcutTarget = path[..(length < 0 ? path.Length : length)].ToString();
						}
					}

					Marshal.FreeCoTaskMem((nint)pidl);
				}
			}

			return Task.CompletedTask;
		}

		private async Task CreateShortcutAsync()
		{
			var extension = DestinationPathExists ? ".lnk" : ".url";

			var shortcutName = FilesystemHelpers.GetShortcutNamingPreference(_shortcutName);
			ShortcutCompleteName = shortcutName + extension;
			var filePath = Path.Combine(WorkingDirectory, ShortcutCompleteName);

			int fileNumber = 1;
			while (Path.Exists(filePath))
			{
				ShortcutCompleteName = shortcutName + $" ({++fileNumber})" + extension;
				filePath = Path.Combine(WorkingDirectory, ShortcutCompleteName);
			}

			ShortcutCreatedSuccessfully = await FileOperationsHelpers.CreateOrUpdateLinkAsync(filePath, FullPath, Arguments);
		}
	}
}
