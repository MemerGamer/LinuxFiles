// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Controls;
using Windows.Foundation.Metadata;

namespace Files.App.Actions
{
	internal sealed partial class CreateAlternateDataStreamAction : BaseUIAction, IAction
	{
		public async Task ExecuteAsync(object? parameter = null)
		{
			var nameDialog = DynamicDialogFactory.GetFor_CreateAlternateDataStreamDialog();
			await nameDialog.TryShowAsync();

			if (nameDialog.DynamicResult != DynamicDialogResult.Primary)
				return;

			var userInput = nameDialog.ViewModel.AdditionalData as string;
			await Task.WhenAll(context.SelectedItems.Select(async selectedItem =>
			{
				var itemPath = selectedItem.ItemPath!;
				var isDateOk = Win32Helper.GetFileDateModified(itemPath, out var dateModified);
				var isReadOnly = Win32Helper.HasFileAttribute(itemPath, System.IO.FileAttributes.ReadOnly);

				// Unset read-only attribute (#7534)
				if (isReadOnly)
					Win32Helper.UnsetFileAttribute(itemPath, System.IO.FileAttributes.ReadOnly);

				if (!Win32Helper.WriteStringToFile($"{itemPath}:{userInput}", ""))
				{
					var dialog = new ContentDialog
					{
						Title = Strings.ErrorCreatingDataStreamTitle.GetLocalizedResource(),
						Content = Strings.ErrorCreatingDataStreamDescription.GetLocalizedResource(),
						PrimaryButtonText = "Ok".GetLocalizedResource()
					};

					if (ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 8))
						dialog.XamlRoot = MainWindow.Instance.Content.XamlRoot;

					await dialog.TryShowAsync();
				}

				// Restore read-only attribute (#7534)
				if (isReadOnly)
					Win32Helper.SetFileAttribute(itemPath, System.IO.FileAttributes.ReadOnly);

				// Restore date modified
				if (isDateOk)
					Win32Helper.SetFileDateModified(itemPath, dateModified);
			}));

			if (context.ShellPage is null)
				return;

			if (FoldersSettingsService.AreAlternateStreamsVisible)
				await context.ShellPage.Refresh_Click();
			else if (ApplicationSettingsService.ShowDataStreamsAreHiddenPrompt)
			{
				var dialog = new ContentDialog
				{
					Title = Strings.DataStreamsAreHiddenTitle.GetLocalizedResource(),
					Content = Strings.DataStreamsAreHiddenDescription.GetLocalizedResource(),
					PrimaryButtonText = Strings.Yes.GetLocalizedResource(),
					SecondaryButtonText = Strings.DontShowAgain.GetLocalizedResource()
				};

				if (ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 8))
					dialog.XamlRoot = MainWindow.Instance.Content.XamlRoot;

				var result = await dialog.TryShowAsync();
				if (result == ContentDialogResult.Primary)
				{
					FoldersSettingsService.AreAlternateStreamsVisible = true;
					await context.ShellPage.Refresh_Click();
				}
				else
					ApplicationSettingsService.ShowDataStreamsAreHiddenPrompt = false;
			}
		}
	}
}
