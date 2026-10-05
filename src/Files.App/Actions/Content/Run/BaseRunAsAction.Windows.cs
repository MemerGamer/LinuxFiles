// Copyright (c) Files Community
// Licensed under the MIT License.



namespace Files.App.Actions
{
	internal abstract partial class BaseRunAsAction : ObservableObject, IAction
	{
		public async Task ExecuteAsync(object? parameter = null)
		{
			await ContextMenu.InvokeVerb(_verb, _context.SelectedItem!.ItemPath);
		}
	}
}
