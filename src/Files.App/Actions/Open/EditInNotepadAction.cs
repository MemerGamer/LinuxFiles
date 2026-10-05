// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class EditInNotepadAction : ObservableObject, IAction
	{
		private readonly IContentPageContext context;

		public string Label
			=> Strings.EditInNotepad.GetLocalizedResource();

		public string Description
			=> Strings.EditInNotepadDescription.GetLocalizedResource();

		public ActionCategory Category
			=> ActionCategory.Open;

		public RichGlyph Glyph
			=> new("\uE70F");

		public bool IsExecutable =>
			OperatingSystem.IsWindows() &&
			context.SelectedItems.Any() &&
			context.PageType != ContentPageTypes.RecycleBin &&
			context.PageType != ContentPageTypes.ZipFolder &&
			context.SelectedItems.All(x => FileExtensionHelpers.IsBatchFile(x.FileExtension) || FileExtensionHelpers.IsAhkFile(x.FileExtension) || FileExtensionHelpers.IsCmdFile(x.FileExtension));

		public EditInNotepadAction()
		{
			context = Ioc.Default.GetRequiredService<IContentPageContext>();

			context.PropertyChanged += Context_PropertyChanged;
		}

#if !WINDOWS
		// LINUX-TODO(launching): this Windows command is hidden on Linux.
		public Task ExecuteAsync(object? parameter = null)
			=> Task.CompletedTask;
#endif

		private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(IContentPageContext.SelectedItems):
					OnPropertyChanged(nameof(IsExecutable));
					break;
			}
		}
	}
}
