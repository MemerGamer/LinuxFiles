// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class InstallCertificateAction : ObservableObject, IAction
	{
		private readonly IContentPageContext context;

		public string Label
			=> Strings.InstallCertificate.GetLocalizedResource();

		public string Description
			=> Strings.InstallCertificateDescription.GetLocalizedFormatResource(context.SelectedItems.Count);

		public RichGlyph Glyph
			=> new("\uEB95");

		public ActionCategory Category
			=> ActionCategory.Install;

		public bool IsExecutable =>
			OperatingSystem.IsWindows() &&
			context.SelectedItems.Any() &&
			context.SelectedItems.All(x => FileExtensionHelpers.IsCertificateFile(x.FileExtension)) &&
			context.PageType != ContentPageTypes.RecycleBin &&
			context.PageType != ContentPageTypes.ZipFolder;

		public InstallCertificateAction()
		{
			context = Ioc.Default.GetRequiredService<IContentPageContext>();

			context.PropertyChanged += Context_PropertyChanged;
		}

#if !WINDOWS
		// LINUX-TODO(install): this Windows command is hidden on Linux.
		public Task ExecuteAsync(object? parameter = null)
			=> Task.CompletedTask;
#endif

		private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(IContentPageContext.SelectedItems))
				OnPropertyChanged(nameof(IsExecutable));
		}
	}
}
