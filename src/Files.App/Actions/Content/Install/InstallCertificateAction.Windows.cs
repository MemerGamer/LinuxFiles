// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;

namespace Files.App.Actions
{
	internal sealed partial class InstallCertificateAction : ObservableObject, IAction
	{
		public async Task ExecuteAsync(object? parameter = null)
		{
			await ContextMenu.InvokeVerb("add", context.SelectedItems.Select(x => x.ItemPath).ToArray());
		}
	}
}
