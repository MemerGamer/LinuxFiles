// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Diagnostics;

namespace Files.App.Helpers
{
	internal static class NavigationItemGuard
	{
		public static bool ShouldSuppress(IShellPage? shellPage)
		{
			var reason = shellPage?.ShellViewModel is not { } viewModel ? "no-shell"
				: !shellPage.IsCurrentInstance || !shellPage.IsCurrentPane ? "inactive-pane"
				: viewModel.IsLoadingItems ? "loading" : null;
			return TraceSuppression(reason);
		}

		public static bool ShouldSuppress(IShellPage? shellPage, ListedItem? item)
		{
			if (ShouldSuppress(shellPage))
				return true;

			// Icon/metadata initialization can lag behind a usable item; only identity is required.
			return TraceSuppression(item is null || string.IsNullOrEmpty(item.ItemPath) ||
				string.IsNullOrEmpty(item.ItemNameRaw) ? "placeholder" : null);
		}

		private static bool TraceSuppression(string? reason)
		{
			if (reason is null)
				return false;

			PerformanceTrace.Begin("navigation-item-guard").Complete(reason);
			return true;
		}

		public static async Task ShowInvalidItemAsync(IShellPage? shellPage, FileSystemStatusCode errorCode, ListedItem? item = null)
		{
			if (item is null ? ShouldSuppress(shellPage) : ShouldSuppress(shellPage, item))
				return;

			PerformanceTrace.Begin("invalid-item-dialog").Complete(errorCode.ToString());
			await DialogDisplayHelper.ShowDialogAsync(Strings.InvalidItemDialogTitle.GetLocalizedResource(),
				string.Format(Strings.InvalidItemDialogContent.GetLocalizedResource(), Environment.NewLine, errorCode.ToString()));
		}
	}
}
