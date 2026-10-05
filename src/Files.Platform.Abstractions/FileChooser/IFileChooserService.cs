// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.FileChooser
{
	/// <summary>
	/// One entry of the file type list. Each pattern is a glob such as <c>*.png</c>.
	/// </summary>
	public sealed record FileChooserFilter(string Name, IReadOnlyList<string> Patterns);

	/// <summary>
	/// What to ask the user for.
	/// </summary>
	public sealed record FileChooserRequest
	{
		/// <summary>Pick a folder instead of a file.</summary>
		public bool PickFolder { get; init; }

		/// <summary>Ask for a destination to write to instead of existing items.</summary>
		public bool Save { get; init; }

		/// <summary>Allow several selections (open only).</summary>
		public bool Multiple { get; init; }

		/// <summary>The dialog title; the desktop picks a default when empty.</summary>
		public string Title { get; init; } = string.Empty;

		public IReadOnlyList<FileChooserFilter> Filters { get; init; } = [];

		/// <summary>The suggested file name (save only).</summary>
		public string? CurrentName { get; init; }

		/// <summary>The folder to start in (save only; the portal ignores it for open).</summary>
		public string? CurrentFolder { get; init; }

		/// <summary>The X11 window id of the parent window, or zero when unknown.</summary>
		public ulong ParentWindowId { get; init; }
	}

	/// <summary>
	/// How the request ended.
	/// </summary>
	public enum FileChooserStatus
	{
		/// <summary>The user chose something.</summary>
		Selected,

		/// <summary>The user dismissed the dialog or it failed.</summary>
		Cancelled,

		/// <summary>No file chooser is available on this desktop.</summary>
		Unavailable,
	}

	/// <summary>
	/// The outcome of a chooser request; <see cref="Paths"/> are absolute local paths.
	/// </summary>
	public sealed record FileChooserResult(FileChooserStatus Status, IReadOnlyList<string> Paths)
	{
		public static FileChooserResult Cancelled { get; } = new(FileChooserStatus.Cancelled, []);

		public static FileChooserResult Unavailable { get; } = new(FileChooserStatus.Unavailable, []);
	}

	/// <summary>
	/// Shows the desktop's own file and folder chooser.
	/// </summary>
	public interface IFileChooserService
	{
		Task<FileChooserResult> ChooseAsync(FileChooserRequest request, CancellationToken cancellationToken = default);
	}
}
