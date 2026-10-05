// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Controls;
using Windows.Foundation.Metadata;

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class CreateAlternateDataStreamAction : BaseUIAction, IAction
	{
		private readonly IContentPageContext context;

		private static readonly IFoldersSettingsService FoldersSettingsService = Ioc.Default.GetRequiredService<IFoldersSettingsService>();
		private static readonly IApplicationSettingsService ApplicationSettingsService = Ioc.Default.GetRequiredService<IApplicationSettingsService>();

		public string Label
			=> Strings.CreateAlternateDataStream.GetLocalizedResource();

		public string Description
			=> Strings.CreateAlternateDataStreamDescription.GetLocalizedFormatResource(context.SelectedItems.Count);

		public ActionCategory Category
			=> ActionCategory.Create;

		public RichGlyph Glyph
			=> new RichGlyph(themedIconStyle: "App.ThemedIcons.AltDataStream");

		public override bool IsExecutable =>
			OperatingSystem.IsWindows() &&
			context.HasSelection &&
			context.CanCreateItem &&
			UIHelpers.CanShowDialog;

		public CreateAlternateDataStreamAction()
		{
			context = Ioc.Default.GetRequiredService<IContentPageContext>();

			context.PropertyChanged += Context_PropertyChanged;
		}

#if !WINDOWS
		// LINUX-TODO(streams): this Windows command is hidden on Linux.
		public Task ExecuteAsync(object? parameter = null)
			=> Task.CompletedTask;
#endif

		private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(IContentPageContext.HasSelection):
				case nameof(IContentPageContext.CanCreateItem):
					OnPropertyChanged(nameof(IsExecutable));
					break;
			}
		}
	}
}
