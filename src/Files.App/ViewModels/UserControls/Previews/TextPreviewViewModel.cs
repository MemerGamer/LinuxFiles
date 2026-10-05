#if !WINDOWS
// Copyright (c) Files Community
// Licensed under the MIT License.

using TextPreview = Files.App.UserControls.FilePreviews.DesktopTextPreview;
using ColorCode;
using Files.App.UserControls.FilePreviews;
using Files.App.ViewModels.Properties;
using System.IO;

namespace Files.App.ViewModels.Previews
{
	public enum TextPreviewKind
	{
		Plain,
		Code,
		Markdown,
	}

	public sealed partial class TextPreviewViewModel : BasePreviewModel
	{
		private string? textValue;
		public string? TextValue
		{
			get => textValue;
			private set => SetProperty(ref textValue, value);
		}

		/// <summary>
		/// How the text is rendered. Everything is rendered as inert styled text; nothing in the file is ever executed or fetched.
		/// </summary>
		public TextPreviewKind Kind { get; init; }

		public ILanguage? Language { get; init; }

		/// <summary>
		/// A note shown below the text, for example when only the beginning of a large file is displayed.
		/// </summary>
		public string? Footer { get; private set; }

		public PreviewTextRenderer.Model? RenderModel { get; private set; }

		private List<FileProperty>? presetDetails;
		private string? encodingName;

		public TextPreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public async override Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var details = new List<FileProperty>();

			try
			{
				if (TextValue is null)
				{
					if (Item.FileSizeBytes is 0)
						return details;
					using var stream = await OpenPreviewReadAsync(LoadCancelledTokenSource.Token);
					if (stream.Length == 0)
						return details;
					Apply(await PreviewTextReader.ReadAsync(stream, LoadCancelledTokenSource.Token));
				}

				var text = TextValue ?? string.Empty;
				RenderModel = await Task.Run(() => PreviewTextRenderer.Parse(text, Kind, Language, LoadCancelledTokenSource.Token), LoadCancelledTokenSource.Token);
				if (presetDetails is not null)
					return presetDetails;
				details.Add(GetFileProperty("PropertyLineCount", CountLines(text)));
				details.Add(GetFileProperty("PropertyWordCount", text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length));

				if (encodingName is not null)
					details.Add(GetFileProperty("Encoding", encodingName));
			}
			catch (OperationCanceledException) when (LoadCancelledTokenSource.IsCancellationRequested) { throw; }
			catch (Exception e)
			{
				Debug.WriteLine(e);
			}

			return details;
		}

		private void Apply(PreviewTextReader.Result result)
		{
			TextValue = result.Text;
			encodingName = result.EncodingName;
			if (result.Truncated)
				Footer = "PreviewTextTruncated".GetLocalizedFormatResource(
					((long)PreviewTextReader.MaxBytes).ToSizeString(), result.TotalBytes.ToSizeString());
		}

		private static int CountLines(string text)
		{
			var lines = 1;
			foreach (var c in text)
				if (c == '\n')
					lines++;
			return lines;
		}

		/// <summary>
		/// Shows <paramref name="text"/> (for example a generated listing) in the text preview with the given details.
		/// </summary>
		public static async Task<TextPreview> CreateFromTextAsync(ListedItem item, string text, List<FileProperty> details, CancellationToken cancellationToken = default)
		{
			var model = new TextPreviewViewModel(item) { TextValue = text, presetDetails = details };
			using var registration = cancellationToken.Register(model.LoadCancelledTokenSource.Cancel);
			await model.LoadAsync();

			return new TextPreview(model);
		}

		/// <summary>
		/// Loads the file with a specific rendering when its extension asks for one (Markdown, highlighted code).
		/// </summary>
		public static async Task<TextPreview?> TryLoadWithKindAsync(ListedItem item, TextPreviewKind kind, ILanguage? language, CancellationToken cancellationToken = default)
		{
			if (item.FileSizeBytes is 0)
				return null;
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (await ResolvePreviewFileAsync(item, cancellationToken) is not { } itemFile)
					return null;

				using var stream = await OpenPreviewReadAsync(itemFile, cancellationToken);
				if (stream.Length == 0)
					return null;
				var result = await PreviewTextReader.ReadAsync(stream, cancellationToken);
				if (result.LooksBinary)
					return null;

				var model = new TextPreviewViewModel(item) { Kind = kind, Language = language };
				model.Apply(result);
				using var registration = cancellationToken.Register(model.LoadCancelledTokenSource.Cancel);
				await model.LoadAsync();

				return new TextPreview(model);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
			catch
			{
				return null;
			}
		}

		public static async Task<TextPreview?> TryLoadAsTextAsync(ListedItem item, CancellationToken cancellationToken = default)
		{
			string? extension = item.FileExtension?.ToLowerInvariant();
			if (ExcludedExtensions(extension) || item.FileSizeBytes is 0)
				return null;

			// The reader caps the bytes read, so large files are previewed by their beginning instead of being skipped.
			return await TryLoadWithKindAsync(item, TextPreviewKind.Plain, null, cancellationToken);
		}

		private static bool ExcludedExtensions(string? extension)
			=> extension is ".iso" or ".pdf";
	}
}
#endif
