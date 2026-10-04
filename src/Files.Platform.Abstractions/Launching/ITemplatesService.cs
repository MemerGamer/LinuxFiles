// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Launching
{
	/// <summary>
	/// The kind of item a <see cref="NewItemTemplate"/> creates.
	/// </summary>
	public enum NewItemKind
	{
		/// <summary>An empty folder.</summary>
		Folder,

		/// <summary>An empty file.</summary>
		EmptyFile,

		/// <summary>A copy of a template file.</summary>
		TemplateFile,
	}

	/// <summary>
	/// An entry of the "New" menu.
	/// </summary>
	/// <param name="Name">The display name.</param>
	/// <param name="Kind">What the entry creates.</param>
	/// <param name="DefaultFileName">The proposed name of the created item, including extension.</param>
	/// <param name="SourcePath">The template file to copy, for <see cref="NewItemKind.TemplateFile"/>.</param>
	public sealed record NewItemTemplate(string Name, NewItemKind Kind, string DefaultFileName, string? SourcePath = null);

	/// <summary>
	/// Provides the entries of the "New" menu and creates items from them.
	/// </summary>
	public interface ITemplatesService
	{
		/// <summary>
		/// Lists the built-in entries followed by the files in <paramref name="templatesDirectory"/> (typically ~/Templates).
		/// </summary>
		Task<IReadOnlyList<NewItemTemplate>> GetTemplatesAsync(string? templatesDirectory, CancellationToken cancellationToken = default);

		/// <summary>
		/// Creates an item in <paramref name="destinationFolder"/>, picking a unique name, and returns its full path.
		/// </summary>
		Task<string> CreateFromTemplateAsync(NewItemTemplate template, string destinationFolder, string? name = null, CancellationToken cancellationToken = default);
	}
}
