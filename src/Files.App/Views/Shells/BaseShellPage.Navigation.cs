// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.EventArguments;
using Files.App.Utils.Storage;
using Files.App.Views.Layouts;
using System;

namespace Files.App.Views.Shells
{
	public abstract partial class BaseShellPage
	{
		public void NavigateWithArguments(Type sourcePageType, NavigationArguments navArgs)
		{
#if !WINDOWS
			// The frame already holds the destination while OnNavigatedTo is still loading it.
			if (!navArgs.IsSearchResultPage && !navArgs.IsLayoutSwitch &&
				ZipStorageFolder.IsZipPath(navArgs.NavPathParam) &&
				ItemDisplay.Content is BaseLayoutPage page && page.NavigationPath == navArgs.NavPathParam)
				return;
#endif
			NavigateToPath(navArgs.NavPathParam, sourcePageType, navArgs);
		}
	}
}
