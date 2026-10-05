// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Archives;
using System.Text;

namespace Files.App.Actions
{
	internal sealed class ArchivePasswordPrompt : IArchivePasswordPrompt
	{
		public async Task<string?> RequestPasswordAsync(string archivePath, bool retry, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var viewModel = new CredentialDialogViewModel { PasswordOnly = true, CanBeAnonymous = false, IsWrongPassword = retry };
			var dialogService = Ioc.Default.GetRequiredService<IDialogService>();
			var result = await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() => dialogService.ShowDialogAsync(viewModel));
			cancellationToken.ThrowIfCancellationRequested();
			if (result != DialogResult.Primary)
				return null;
			if (viewModel.Password is not { } password)
				return null;
			using (password)
				return Encoding.UTF8.GetString(password);
		}
	}
}
