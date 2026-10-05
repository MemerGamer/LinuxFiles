// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Controls;
using Files.Platform.Abstractions.Wallpaper;
using Windows.Foundation.Metadata;

namespace Files.App.Actions
{
	internal abstract class BaseSetAsAction : ObservableObject, IAction
	{
		protected readonly IContentPageContext ContentPageContext = Ioc.Default.GetRequiredService<IContentPageContext>();
		protected readonly IWindowsWallpaperService WindowsWallpaperService = Ioc.Default.GetRequiredService<IWindowsWallpaperService>();

		public abstract string Label { get; }

		public virtual string ExtendedLabel
			=> Label;

		public abstract string Description { get; }

		public virtual ActionCategory Category
			=> ActionCategory.Image;

		public abstract RichGlyph Glyph { get; }

		public virtual bool IsExecutable =>
			ContentPageContext.ShellPage is not null &&
			ContentPageContext.PageType != ContentPageTypes.RecycleBin &&
			ContentPageContext.PageType != ContentPageTypes.ZipFolder &&
			ContentPageContext.PageType != ContentPageTypes.ReleaseNotes &&
			ContentPageContext.PageType != ContentPageTypes.Settings &&
			(ContentPageContext.ShellPage?.SlimContentPage?.SelectedItemsPropertiesViewModel?.IsCompatibleToSetAsWindowsWallpaper ?? false);

		private static volatile int portalState; // 0 = unknown, 1 = available, 2 = unavailable

		/// <summary>Whether the desktop's wallpaper portal answered. Only meaningful off Windows; hides the action until known.</summary>
		protected static bool IsWallpaperPortalAvailable => portalState == 1;

		public BaseSetAsAction()
		{
			ContentPageContext.PropertyChanged += ContentPageContext_PropertyChanged;

			if (!OperatingSystem.IsWindows() && portalState == 0)
				_ = ProbeWallpaperPortalAsync();
		}

		private async Task ProbeWallpaperPortalAsync()
		{
			var service = Ioc.Default.GetService<IWallpaperService>();
			var available = service is not null && await service.IsAvailableAsync();
			portalState = available ? 1 : 2;
			OnPropertyChanged(nameof(IsExecutable));
		}

		public abstract Task ExecuteAsync(object? parameter = null);

		protected async Task SetThroughPortalAsync(string path, WallpaperTarget target)
		{
			var service = Ioc.Default.GetService<IWallpaperService>();
			var result = service is null ? WallpaperResult.Unavailable : await service.SetAsync(path, target);
			if (result is WallpaperResult.Failed or WallpaperResult.Unavailable)
				ShowErrorDialog(Strings.FailedToSetBackground.GetLocalizedResource());
		}

		protected async void ShowErrorDialog(string message)
		{
			var errorDialog = new ContentDialog()
			{
				Title = Strings.FailedToSetBackground.GetLocalizedResource(),
				Content = message,
				PrimaryButtonText = Strings.OK.GetLocalizedResource(),
			};

			if (ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 8))
				errorDialog.XamlRoot = MainWindow.Instance.Content.XamlRoot;

			await errorDialog.TryShowAsync();
		}

		private void ContentPageContext_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(IContentPageContext.PageType):
					OnPropertyChanged(nameof(IsExecutable));
					break;
				case nameof(IContentPageContext.SelectedItem):
				case nameof(IContentPageContext.SelectedItems):
					{
						if (ContentPageContext.ShellPage is not null && ContentPageContext.ShellPage.SlimContentPage is not null)
						{
							var viewModel = ContentPageContext.ShellPage.SlimContentPage.SelectedItemsPropertiesViewModel;
							var extensions = ContentPageContext.SelectedItems.Select(selectedItem => selectedItem.FileExtension).Distinct().ToList();

							viewModel.CheckAllFileExtensions(extensions);
						}

						OnPropertyChanged(nameof(IsExecutable));
						break;
					}
			}
		}
	}
}
