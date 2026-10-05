// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Files.App.Dialogs;
using Microsoft.UI.Xaml.Controls;
using System.Text;



namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class DecompressArchiveToChildFolderAction : BaseDecompressArchiveAction
	{
		public override string Label
			=> ComputeLabel();

		public override string Description
			=> Strings.DecompressArchiveToChildFolderDescription.GetLocalizedFormatResource(context.SelectedItems.Count);

		public string AccessKey
			=> "C";

		public DecompressArchiveToChildFolderAction()
		{
		}

		public override async Task ExecuteAsync(object? parameter = null)
		{
			if (context.SelectedItems.Count is 0)
				return;

			foreach (var selectedItem in context.SelectedItems)
			{
				var password = string.Empty;

				var archivePath = selectedItem.ItemPath;
				var currentFolderPath = context.ShellPage?.ShellViewModel?.CurrentFolder?.ItemPath;
				if (string.IsNullOrEmpty(archivePath) || !SystemIO.File.Exists(archivePath) || string.IsNullOrEmpty(currentFolderPath))
					return;

				if (await FilesystemTasks.Wrap(() => StorageArchiveService.IsEncryptedAsync(archivePath)))
				{
					DecompressArchiveDialogViewModel decompressArchiveViewModel = new(archivePath)
					{
						IsArchiveEncrypted = true,
						ShowPathSelection = false
					};
					DecompressArchiveDialog decompressArchiveDialog = new() { ViewModel = decompressArchiveViewModel };

					decompressArchiveDialog.XamlRoot = MainWindow.Instance.Content.XamlRoot;

					ContentDialogResult option = await decompressArchiveDialog.TryShowAsync();
					if (option != ContentDialogResult.Primary)
						return;

					password = Encoding.UTF8.GetString(decompressArchiveViewModel.Password!);
				}

				var destinationFolderPath = SystemIO.Path.Combine(currentFolderPath,
					Ioc.Default.GetRequiredService<Files.Platform.Abstractions.Archives.IArchiveService>().GetDefaultExtractFolderName(archivePath));

				// Operate decompress
				await FilesystemTasks.Wrap(() =>
					StorageArchiveService.DecompressAsync(selectedItem.ItemPath!, destinationFolderPath, password));
			}
		}

		protected override void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(IContentPageContext.SelectedItems):
				case nameof(IContentPageContext.Folder):
					{
						if (IsContextPageTypeAdaptedToCommand())
						{
							OnPropertyChanged(nameof(Label));
							OnPropertyChanged(nameof(IsExecutable));
						}

						break;
					}
			}
		}

		private string ComputeLabel()
			=> ToPlatformSeparator(ComputeLabelCore());

		private static string ToPlatformSeparator(string label)
			=> SystemIO.Path.DirectorySeparatorChar == '\\' ? label : label.Replace('\\', SystemIO.Path.DirectorySeparatorChar);

		private string ComputeLabelCore()
		{
			if (context.SelectedItems == null || context.SelectedItems.Count == 0)
				return string.Format(Strings.BaseLayoutItemContextFlyoutExtractToChildFolder.GetLocalizedResource(), string.Empty);

			return context.SelectedItems.Count > 1
				? string.Format(Strings.BaseLayoutItemContextFlyoutExtractToChildFolder.GetLocalizedResource(), "*")
				: string.Format(Strings.BaseLayoutItemContextFlyoutExtractToChildFolder.GetLocalizedResource(), SystemIO.Path.GetFileNameWithoutExtension(context.SelectedItems.First().Name));
		}
	}
}

#endif
