// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Fonts;
using Files.Shared.Helpers;
using Microsoft.UI.Xaml.Controls;

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class InstallFontAction : ObservableObject, IAction
	{
		private readonly IContentPageContext context;
		private static readonly StatusCenterViewModel StatusCenterViewModel = Ioc.Default.GetRequiredService<StatusCenterViewModel>();

		public string Label
			=> Strings.InstallFont.GetLocalizedResource();

		public string Description
			=> Strings.InstallFontDescription.GetLocalizedFormatResource(context.SelectedItems.Count);

		public RichGlyph Glyph
			=> new(themedIconStyle: "App.ThemedIcons.Actions.FontInstall");

		public ActionCategory Category
			=> ActionCategory.Install;

		public bool IsExecutable =>
			context.SelectedItems.Any() &&
			context.SelectedItems.All(x => FileExtensionHelpers.IsFontFile(x.FileExtension)) &&
			context.PageType != ContentPageTypes.RecycleBin &&
			context.PageType != ContentPageTypes.ZipFolder;

		public InstallFontAction()
		{
			context = Ioc.Default.GetRequiredService<IContentPageContext>();

			context.PropertyChanged += Context_PropertyChanged;
		}

		public async Task ExecuteAsync(object? parameter = null)
		{
			if (context.ShellPage?.ShellViewModel?.WorkingDirectory is not { } workingDirectory)
				return;

			var banner = StatusCenterHelper.AddCard_InstallFont(workingDirectory.CreateEnumerable(), ReturnResult.InProgress, context.SelectedItems.Count);
			banner.IsCancelable = false;

			var paths = context.SelectedItems.Select(item => item.ItemPath!).ToArray();
			if (OperatingSystem.IsWindows())
				await Win32Helper.InstallFontsAsync(paths, false);
			else
				await InstallForCurrentUserAsync(paths);

			StatusCenterViewModel.RemoveItem(banner);
			var currentWorkingDirectory = context.ShellPage.GetRequiredShellViewModel().WorkingDirectory!;
			StatusCenterHelper.AddCard_InstallFont(currentWorkingDirectory.CreateEnumerable(), ReturnResult.Success, context.SelectedItems.Count);
		}

		// Per-user install into ~/.local/share/fonts; "for all users" stays Windows-only
		private static async Task InstallForCurrentUserAsync(string[] paths)
		{
			var installer = Ioc.Default.GetRequiredService<IFontInstallService>();
			foreach (var path in paths)
			{
				var name = SystemIO.Path.GetFileName(path);
				var result = await installer.InstallAsync(path, overwrite: false);
				if (result is FontInstallResult.AlreadyExists)
				{
					var replace = new ContentDialog()
					{
						Title = Strings.InstallFont.GetLocalizedResource(),
						Content = string.Format(Strings.FontAlreadyInstalledPrompt.GetLocalizedResource(), name),
						PrimaryButtonText = Strings.ReplaceExisting.GetLocalizedResource(),
						CloseButtonText = Strings.Cancel.GetLocalizedResource(),
						XamlRoot = MainWindow.Instance.Content.XamlRoot,
					};

					if (await replace.TryShowAsync() != ContentDialogResult.Primary)
						continue;

					result = await installer.InstallAsync(path, overwrite: true);
				}

				if (result is FontInstallResult.NotAFont or FontInstallResult.Failed)
				{
					var error = new ContentDialog()
					{
						Title = Strings.InstallFont.GetLocalizedResource(),
						Content = string.Format(Strings.FontInstallFailed.GetLocalizedResource(), name),
						PrimaryButtonText = Strings.OK.GetLocalizedResource(),
						XamlRoot = MainWindow.Instance.Content.XamlRoot,
					};

					await error.TryShowAsync();
				}
			}
		}

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
