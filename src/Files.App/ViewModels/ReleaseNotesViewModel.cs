// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.App.UserControls.FilePreviews;
using Files.App.ViewModels.Previews;

namespace Files.App.ViewModels
{
	public sealed partial class ReleaseNotesViewModel : ObservableObject
	{
		public string BlogPostUrl =>
			Constants.ExternalUrl.ReleaseNotesUrl;

		public string SupportUsUrl =>
			Constants.ExternalUrl.SupportUsUrl;

		public ReleaseNotesViewModel()
		{
		}

		/// <summary>
		/// Fetches and parses the LinuxFiles release notes for the running version; null means the fallback should be shown.
		/// </summary>
		public async Task<PreviewTextRenderer.Model?> LoadLinuxReleaseNotesAsync(CancellationToken cancellationToken)
		{
			var markdown = await ReleaseNotesHelper.GetLinuxReleaseNotesAsync(cancellationToken);
			if (markdown is null)
				return null;

			return await Task.Run(() => PreviewTextRenderer.Parse(markdown, TextPreviewKind.Markdown, null, cancellationToken), cancellationToken);
		}
	}
}
