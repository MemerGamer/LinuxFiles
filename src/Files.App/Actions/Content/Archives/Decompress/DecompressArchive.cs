// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Files.App.Dialogs;
using Files.Shared.Helpers;
using Microsoft.UI.Xaml.Controls;
using System.IO;
using System.Text;



namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class DecompressArchiveAction : BaseDecompressArchiveAction
	{
		private readonly IUserSettingsService UserSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();

		public override string Label
			=> Strings.ExtractFiles.GetLocalizedResource();

		public override string Description
			=> Strings.DecompressArchiveDescription.GetLocalizedFormatResource(context.SelectedItems.Count);

		public string AccessKey
			=> "E";

		public override HotKey HotKey
			=> new(Keys.E, KeyModifiers.Ctrl);

		public DecompressArchiveAction()
		{
		}

		public override async Task ExecuteAsync(object? parameter = null)
		{
			if (context.ShellPage is null)
				return;

			var archivePath = GetArchivePath();

			if (string.IsNullOrEmpty(archivePath))
				return;

			if (!File.Exists(archivePath))
				return;

			var isArchiveEncrypted = await FilesystemTasks.Wrap(() => StorageArchiveService.IsEncryptedAsync(archivePath));
			var isArchiveEncodingUndetermined = await FilesystemTasks.Wrap(() => StorageArchiveService.IsEncodingUndeterminedAsync(archivePath));
			Encoding? detectedEncoding = null;
			if (isArchiveEncodingUndetermined)
			{
				detectedEncoding = await FilesystemTasks.Wrap(() => StorageArchiveService.DetectEncodingAsync(archivePath));
			}
			var password = string.Empty;
			Encoding? encoding = null;

			DecompressArchiveDialogViewModel decompressArchiveViewModel = new(archivePath)
			{
				IsArchiveEncrypted = isArchiveEncrypted,
				IsArchiveEncodingUndetermined = isArchiveEncodingUndetermined,
				ShowPathSelection = true,
				DetectedEncoding = detectedEncoding,
			};
			DecompressArchiveDialog decompressArchiveDialog = new() { ViewModel = decompressArchiveViewModel };

			decompressArchiveDialog.XamlRoot = MainWindow.Instance.Content.XamlRoot;

			ContentDialogResult option = await decompressArchiveDialog.TryShowAsync();
			if (option != ContentDialogResult.Primary)
				return;

			if (isArchiveEncrypted && decompressArchiveViewModel.Password is not null)
				password = Encoding.UTF8.GetString(decompressArchiveViewModel.Password);

			encoding = decompressArchiveViewModel.SelectedEncoding.Encoding;

			// Check if archive still exists
			if (!File.Exists(archivePath))
				return;

			string destinationFolderPath = decompressArchiveViewModel.DestinationFolderPath;

			// Save extraction location for future use
			SaveExtractionLocation(destinationFolderPath);


			// Operate decompress
			var result = await FilesystemTasks.Wrap(() =>
				StorageArchiveService.DecompressAsync(archivePath, destinationFolderPath, password, encoding));

			if (result && decompressArchiveViewModel.OpenDestinationFolderOnCompletion)
				await NavigationHelpers.OpenPath(destinationFolderPath, context.ShellPage, FilesystemItemType.Directory);
		}

		protected override bool CanDecompressInsideArchive()
		{
			return
				context.PageType == ContentPageTypes.ZipFolder &&
				!context.HasSelection &&
				context.Folder is not null &&
				FileExtensionHelpers.IsZipFile(Path.GetExtension(context.Folder.ItemPath));
		}

		protected override bool CanDecompressSelectedItems()
		{
			return context.SelectedItems.Count == 1 && base.CanDecompressSelectedItems();
		}

		private string? GetArchivePath()
		{
			if (!string.IsNullOrEmpty(context.SelectedItem?.ItemPath))
				return context.SelectedItem?.ItemPath;

			if (context.PageType == ContentPageTypes.ZipFolder && !context.HasSelection)
				return context.Folder?.ItemPath;

			return null;
		}

		private void SaveExtractionLocation(string path)
		{
			var previousArchiveExtractionLocations = UserSettingsService.GeneralSettingsService.PreviousArchiveExtractionLocations?.ToList() ?? [];
			previousArchiveExtractionLocations.Remove(path);
			previousArchiveExtractionLocations.Insert(0, path);

			if (previousArchiveExtractionLocations.Count > 10)
				UserSettingsService.GeneralSettingsService.PreviousArchiveExtractionLocations = previousArchiveExtractionLocations.RemoveFrom(11);
			else
				UserSettingsService.GeneralSettingsService.PreviousArchiveExtractionLocations = previousArchiveExtractionLocations;
		}
	}
}

#endif
