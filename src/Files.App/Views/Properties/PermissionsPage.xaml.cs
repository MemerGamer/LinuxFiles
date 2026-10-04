// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.IO;

namespace Files.App.Views.Properties
{
	/// <summary>
	/// The Linux replacement of the Windows Security page: POSIX owner, group and permission bits.
	/// </summary>
	public sealed partial class PermissionsPage : BasePropertiesPage
	{
		public PermissionsViewModel PermissionsViewModel { get; private set; } = null!;

		public PermissionsPage()
		{
			InitializeComponent();
		}

		protected override void OnNavigatedTo(NavigationEventArgs e)
		{
			var parameter = (PropertiesPageNavigationParameter)e.Parameter;
			var path = parameter.Parameter switch
			{
				ListedItem item => item.ItemPath,
				_ => null,
			};

			PermissionsViewModel = new PermissionsViewModel(path ?? string.Empty);
			Bindings.Update();

			base.OnNavigatedTo(e);
		}

		public override async Task<bool> SaveChangesAsync()
		{
			var vm = PermissionsViewModel;
			if (!vm.IsAvailable || !vm.CanChangeMode)
				return true;

			var (setBits, clearBits) = vm.GetChangedBits();
			var recursive = vm.IsDirectory && vm.ApplyRecursively && (setBits != 0 || clearBits != 0);

			if (recursive)
			{
				var dialog = new ContentDialog
				{
					Title = Strings.PropertiesPermissionsRecursiveTitle.GetLocalizedResource(),
					Content = Strings.PropertiesPermissionsRecursiveContent.GetLocalizedResource(),
					PrimaryButtonText = Strings.PropertiesPermissionsRecursiveConfirm.GetLocalizedResource(),
					CloseButtonText = Strings.Cancel.GetLocalizedResource(),
					DefaultButton = ContentDialogButton.Close,
					XamlRoot = XamlRoot,
				};

				if (await dialog.ShowAsync() != ContentDialogResult.Primary)
					return false;
			}

			try
			{
				var failed = await vm.ApplyAsync(recursive, default);
				if (failed > 0)
					await ShowMessageAsync(string.Format(Strings.PropertiesPermissionsApplyFailed.GetLocalizedResource(), failed));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
			{
				await ShowMessageAsync(ex.Message);
				return false;
			}

			return true;
		}

		private async Task ShowMessageAsync(string message)
		{
			var dialog = new ContentDialog
			{
				Title = Strings.Permissions.GetLocalizedResource(),
				Content = message,
				CloseButtonText = Strings.Close.GetLocalizedResource(),
				XamlRoot = XamlRoot,
			};

			await dialog.ShowAsync();
		}

		public override void Dispose()
		{
		}
	}
}
