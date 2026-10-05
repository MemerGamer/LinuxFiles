// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class RunWithPowershellAction : ObservableObject, IAction
	{
		private readonly IContentPageContext context;

		public string Label
			=> Strings.RunWithPowerShell.GetLocalizedResource();

		public string Description
			=> Strings.RunWithPowershellDescription.GetLocalizedResource();

		public ActionCategory Category
			=> ActionCategory.Run;

		public RichGlyph Glyph
			=> new("\uE756");

		public bool IsExecutable =>
			OperatingSystem.IsWindows() &&
			context.SelectedItem is not null &&
			context.PageType != ContentPageTypes.RecycleBin &&
			FileExtensionHelpers.IsPowerShellFile(context.SelectedItem.FileExtension);

		public RunWithPowershellAction()
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
				case nameof(IContentPageContext.Folder):
					OnPropertyChanged(nameof(IsExecutable));
					break;
			}
		}
	}
}
