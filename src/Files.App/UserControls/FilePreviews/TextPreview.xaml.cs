// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Previews;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace Files.App.UserControls.FilePreviews
{
	public sealed partial class TextPreview : UserControl
	{
		public TextPreview(TextPreviewViewModel viewModel)
		{
			ViewModel = viewModel;
			InitializeComponent();
			ActualThemeChanged += (_, _) => BuildContent();
			BuildContent();
		}

		public TextPreviewViewModel ViewModel { get; set; }

		private void BuildContent()
		{
			ContentBlock.Blocks.Clear();
			var text = ViewModel.TextValue ?? string.Empty;
			var dark = ActualTheme == ElementTheme.Dark
				|| (ActualTheme == ElementTheme.Default && Application.Current?.RequestedTheme == ApplicationTheme.Dark);

			switch (ViewModel.Kind)
			{
				case TextPreviewKind.Markdown:
					PreviewTextRenderer.AddMarkdownBlocks(ContentBlock.Blocks, text, dark);
					break;
				default:
				{
					// Monospace keeps code, logs and archive listings aligned.
					ContentBlock.FontFamily = PreviewTextRenderer.GetMonospaceFont();
					ContentBlock.FontSize = 12;
					var paragraph = new Paragraph();
					if (ViewModel.Kind is TextPreviewKind.Code)
						PreviewTextRenderer.AddCodeInlines(paragraph, text, ViewModel.Language, dark);
					else
						paragraph.Inlines.Add(new Run { Text = text });
					ContentBlock.Blocks.Add(paragraph);
					break;
				}
			}

			if (!string.IsNullOrEmpty(ViewModel.Footer))
			{
				FooterText.Text = ViewModel.Footer;
				FooterText.Visibility = Visibility.Visible;
			}
		}
	}
}
