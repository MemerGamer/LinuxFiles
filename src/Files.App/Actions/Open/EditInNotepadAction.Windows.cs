// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;

namespace Files.App.Actions
{
	internal sealed partial class EditInNotepadAction : ObservableObject, IAction
	{
		public Task ExecuteAsync(object? parameter = null)
		{
			return Task.WhenAll(context.SelectedItems.Select(item => Win32Helper.RunPowershellCommandAsync($"& 'notepad.exe' {Win32Helper.ToPowerShellStringLiteral(item.ItemPath)}", PowerShellExecutionOptions.Hidden)));
		}
	}
}
