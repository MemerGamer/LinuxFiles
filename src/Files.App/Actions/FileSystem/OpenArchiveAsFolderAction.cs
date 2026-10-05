// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;
using Windows.Storage;

namespace Files.App.Actions
{
	/// <summary>
	/// Browses an archive like a folder (Linux), while double-click keeps opening it in the default application.
	/// </summary>
	[GeneratedRichCommand]
	internal sealed partial class OpenArchiveAsFolderAction : ObservableObject, IAction
	{
		private readonly IContentPageContext context;

		public string Label
			=> Strings.OpenArchiveAsFolder.GetLocalizedResource();

		public string Description
			=> Strings.OpenArchiveAsFolderDescription.GetLocalizedResource();

		public ActionCategory Category
			=> ActionCategory.Open;

		public RichGlyph Glyph
			=> new(themedIconStyle: "App.ThemedIcons.Folder");

		public bool IsExecutable =>
			!OperatingSystem.IsWindows() &&
			context.ShellPage is not null &&
			context.PageType is ContentPageTypes.Folder &&
			context.SelectedItems.Count == 1 &&
			context.SelectedItem is { PrimaryItemAttribute: StorageItemTypes.File, ItemPath: { } path } &&
			FileExtensionHelpers.IsZipPath(path);

		public OpenArchiveAsFolderAction()
		{
			context = Ioc.Default.GetRequiredService<IContentPageContext>();

			context.PropertyChanged += Context_PropertyChanged;
		}

		public Task ExecuteAsync(object? parameter = null)
		{
			if (context.ShellPage is not { } shellPage || context.SelectedItem?.ItemPath is not { } path || !IsExecutable)
				return Task.CompletedTask;

			shellPage.NavigateWithArguments(shellPage.InstanceViewModel.FolderSettings.GetLayoutType(path), new NavigationArguments()
			{
				NavPathParam = path,
				AssociatedTabInstance = shellPage,
			});

			return Task.CompletedTask;
		}

		private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName is nameof(IContentPageContext.SelectedItems) or nameof(IContentPageContext.PageType) or nameof(IContentPageContext.HasSelection))
				OnPropertyChanged(nameof(IsExecutable));
		}
	}
}
