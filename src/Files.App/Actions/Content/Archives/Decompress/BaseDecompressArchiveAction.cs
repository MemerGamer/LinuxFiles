// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Files.App.Dialogs;
using Microsoft.UI.Xaml.Controls;
using System.Text;



namespace Files.App.Actions
{
	internal abstract class BaseDecompressArchiveAction : BaseUIAction, IAction
	{
		protected readonly IContentPageContext context;
		protected IStorageArchiveService StorageArchiveService { get; } = Ioc.Default.GetRequiredService<IStorageArchiveService>();

		public abstract string Label { get; }

		public abstract string Description { get; }

		public virtual ActionCategory Category
			=> ActionCategory.Archive;

		public virtual HotKey HotKey
			=> HotKey.None;

		public override bool IsExecutable =>
			(IsContextPageTypeAdaptedToCommand() &&
			CanDecompressSelectedItems() ||
			CanDecompressInsideArchive()) &&
			UIHelpers.CanShowDialog;

		public BaseDecompressArchiveAction()
		{
			context = Ioc.Default.GetRequiredService<IContentPageContext>();

			context.PropertyChanged += Context_PropertyChanged;
		}

		public abstract Task ExecuteAsync(object? parameter = null);

		protected bool IsContextPageTypeAdaptedToCommand()
		{
			return
				context.PageType != ContentPageTypes.RecycleBin &&
				context.PageType != ContentPageTypes.ZipFolder &&
				context.PageType != ContentPageTypes.ReleaseNotes &&
				context.PageType != ContentPageTypes.Settings &&
				context.PageType != ContentPageTypes.None;
		}

		protected async Task DecompressArchiveHereAsync(bool smart = false)
		{
			if (context.SelectedItems.Count is 0)
				return;

			var selectedItems = context.SelectedItems.ToList();
			var currentFolderPath = context.ShellPage?.ShellViewModel?.CurrentFolder?.ItemPath ?? string.Empty;

			foreach (var selectedItem in selectedItems)
			{
				var password = string.Empty;
				var archivePath = selectedItem.ItemPath;
				if (string.IsNullOrEmpty(archivePath) || !SystemIO.File.Exists(archivePath))
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

					if (decompressArchiveViewModel.Password is not null)
						password = Encoding.UTF8.GetString(decompressArchiveViewModel.Password);
				}

				var destinationFolderPath = currentFolderPath;
				var isMultipleItems = await StorageArchiveService.HasMultipleTopLevelEntriesAsync(archivePath, password);
				if (smart && isMultipleItems)
					destinationFolderPath = SystemIO.Path.Combine(currentFolderPath,
						Ioc.Default.GetRequiredService<Files.Platform.Abstractions.Archives.IArchiveService>().GetDefaultExtractFolderName(archivePath));

				// Operate decompress
				var result = await FilesystemTasks.Wrap(() =>
					StorageArchiveService.DecompressAsync(selectedItem.ItemPath!, destinationFolderPath, password));
			}
		}

		protected virtual bool CanDecompressInsideArchive()
		{
			return false;
		}

		protected virtual bool CanDecompressSelectedItems()
		{
			return StorageArchiveService.CanDecompress(context.SelectedItems);
		}

		protected virtual void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
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

#endif
