// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;

namespace Files.App.Actions
{
	internal sealed partial class RunWithPowershellAction : ObservableObject, IAction
	{
		public Task ExecuteAsync(object? parameter = null)
		{
			var itemPath = context.ShellPage?.SlimContentPage?.SelectedItem?.ItemPath;
			return Win32Helper.RunPowershellCommandAsync(
				$"& {Win32Helper.ToPowerShellStringLiteral(itemPath)}",
				PowerShellExecutionOptions.None,
				context.Folder?.ItemPath
			);
		}
	}
}
