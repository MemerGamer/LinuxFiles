#if !WINDOWS
// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Previews;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace Files.App.UserControls.FilePreviews
{
	public sealed partial class DesktopTextPreview : UserControl
	{
		public DesktopTextPreview(TextPreviewViewModel viewModel)
		{
			ViewModel = viewModel;
			InitializeComponent();
			ActualThemeChanged += (_, _) => BuildContent();
			BuildContent();
		}

		public TextPreviewViewModel ViewModel { get; set; }

		private void BuildContent()
		{
			ContentPanel.Children.Clear();
			var model = ViewModel.RenderModel;
			if (model is null)
				return;
			var dark = ActualTheme == ElementTheme.Dark
				|| (ActualTheme == ElementTheme.Default && Application.Current?.RequestedTheme == ApplicationTheme.Dark);

			// RichTextBlock draws nothing on Uno Skia, so the paragraphs are built in a detached block
			// collection and each one is shown in its own wrapping TextBlock.
			var blocks = new RichTextBlock().Blocks;
			switch (ViewModel.Kind)
			{
				case TextPreviewKind.Markdown:
					PreviewTextRenderer.AddMarkdownBlocks(blocks, model, dark);
					break;
				default:
				{
					// Monospace keeps code, logs and archive listings aligned.
					var paragraph = new Paragraph
					{
						FontFamily = PreviewTextRenderer.GetMonospaceFont(),
						FontSize = 12,
					};
					if (ViewModel.Kind is TextPreviewKind.Code)
						PreviewTextRenderer.AddCodeInlines(paragraph, model, dark);
					else
						PreviewTextRenderer.AddPlainInlines(paragraph.Inlines, model.Text);
					blocks.Add(paragraph);
					break;
				}
			}

			foreach (var block in blocks.OfType<Paragraph>())
			{
				var textBlock = new TextBlock
				{
					IsTextSelectionEnabled = true,
					TextWrapping = TextWrapping.Wrap,
					Margin = block.Margin,
					FontFamily = block.FontFamily,
					FontSize = block.FontSize,
					FontWeight = block.FontWeight,
					FontStyle = block.FontStyle,
				};
				foreach (var inline in block.Inlines.ToList())
				{
					block.Inlines.Remove(inline);
					textBlock.Inlines.Add(inline);
				}

				ContentPanel.Children.Add(textBlock);
			}

			if (!string.IsNullOrEmpty(ViewModel.Footer))
			{
				FooterText.Text = ViewModel.Footer;
				FooterText.Visibility = Visibility.Visible;
			}
		}
	}
}

#endif
