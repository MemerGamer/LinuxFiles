// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class InstallInfDriverAction : ObservableObject, IAction
	{
		private readonly IContentPageContext context;

		public string Label
			=> Strings.InstallDriver.GetLocalizedResource();

		public string Description
			=> Strings.InstallInfDriverDescription.GetLocalizedFormatResource(context.SelectedItems.Count);

		public RichGlyph Glyph
			=> new("\uE9F5");

		public ActionCategory Category
			=> ActionCategory.Install;

		public bool IsExecutable =>
			OperatingSystem.IsWindows() &&
			context.SelectedItems.Count == 1 &&
			FileExtensionHelpers.IsInfFile(context.SelectedItems[0].FileExtension) &&
			context.PageType != ContentPageTypes.RecycleBin &&
			context.PageType != ContentPageTypes.ZipFolder;

		public InstallInfDriverAction()
		{
			context = Ioc.Default.GetRequiredService<IContentPageContext>();

			context.PropertyChanged += Context_PropertyChanged;
		}

#if !WINDOWS
		// LINUX-TODO(install): this Windows command is hidden on Linux.
		public Task ExecuteAsync(object? parameter = null)
			=> Task.CompletedTask;
#endif

		public void Context_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(IContentPageContext.SelectedItems):
				case nameof(IContentPageContext.PageType):
					OnPropertyChanged(nameof(IsExecutable));
					break;
			}
		}
	}
}
