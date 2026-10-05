// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class OpenClassicPropertiesAction : ObservableObject, IAction
	{
		private readonly IContentPageContext context;

		public string Label
			=> Strings.OpenClassicProperties.GetLocalizedResource();

		public string Description
			=> Strings.OpenClassicPropertiesDescription.GetLocalizedResource();

		public ActionCategory Category
			=> ActionCategory.Open;

		public RichGlyph Glyph
			=> new(themedIconStyle: "App.ThemedIcons.Properties");

		public HotKey HotKey
			=> new(Keys.Enter, KeyModifiers.AltShift);

		public bool IsExecutable =>
			// Opens the Windows shell's properties dialog; there's no equivalent on Linux.
			OperatingSystem.IsWindows() &&
			context.PageType is not ContentPageTypes.Home &&
			context.PageType is not ContentPageTypes.ReleaseNotes &&
			context.PageType is not ContentPageTypes.Settings &&
			(context.HasSelection && context.SelectedItems.Count == 1 ||
			!context.HasSelection && context.PageType is not ContentPageTypes.SearchResults);

		public OpenClassicPropertiesAction()
		{
			context = Ioc.Default.GetRequiredService<IContentPageContext>();

			context.PropertyChanged += Context_PropertyChanged;
		}

#if !WINDOWS
		// LINUX-TODO(properties): the Windows shell properties command is hidden on Linux.
		public Task ExecuteAsync(object? parameter = null)
			=> Task.CompletedTask;
#endif


		private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(IContentPageContext.PageType):
				case nameof(IContentPageContext.HasSelection):
				case nameof(IContentPageContext.Folder):
					OnPropertyChanged(nameof(IsExecutable));
					break;
			}
		}
	}
}
