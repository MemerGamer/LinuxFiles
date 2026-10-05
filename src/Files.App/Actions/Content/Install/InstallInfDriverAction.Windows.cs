// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;

namespace Files.App.Actions
{
	internal sealed partial class InstallInfDriverAction : ObservableObject, IAction
	{
		public async Task ExecuteAsync(object? parameter = null)
		{
			await Task.WhenAll(context.SelectedItems.Select(selectedItem => Win32Helper.InstallInf(selectedItem.ItemPath)));
		}
	}
}
