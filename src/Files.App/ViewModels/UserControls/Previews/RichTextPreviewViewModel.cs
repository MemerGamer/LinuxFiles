// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Windows.Storage.Streams;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class RichTextPreviewViewModel : BasePreviewModel
	{
		public IRandomAccessStream? Stream { get; set; }

		public RichTextPreviewViewModel(ListedItem item) : base(item) { }

		public async override Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			// LINUX-TODO(preview): no RTF renderer on desktop
			await Task.CompletedTask;
			throw new NotSupportedException();
		}
	}
}
