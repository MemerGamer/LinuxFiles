// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Icons
{
	/// <summary>
	/// The file an icon name resolved to.
	/// </summary>
	/// <param name="Path">The absolute path of the icon file.</param>
	/// <param name="IsSvg">Whether the file is an SVG (otherwise PNG or XPM).</param>
	public readonly record struct IconLookupResult(string Path, bool IsSvg);

	/// <summary>
	/// Resolves freedesktop icon names to files in the current icon theme.
	/// </summary>
	public interface IIconThemeProvider
	{
		/// <summary>
		/// Gets the name of the current icon theme.
		/// </summary>
		string CurrentThemeName { get; }

		/// <summary>
		/// Resolves an icon name (or absolute path) to a file, or <see langword="null"/> when not found.
		/// </summary>
		/// <param name="iconName">The icon name, for example <c>folder-documents</c>.</param>
		/// <param name="size">The desired size in logical pixels.</param>
		/// <param name="scale">The display scale factor.</param>
		/// <param name="cancellationToken">A token to cancel the operation.</param>
		Task<IconLookupResult?> ResolveIconAsync(string iconName, uint size, int scale = 1, CancellationToken cancellationToken = default);

		/// <summary>
		/// Resolves the first name in <paramref name="iconNames"/> that exists in the theme, or <see langword="null"/>.
		/// </summary>
		Task<IconLookupResult?> ResolveIconAsync(IReadOnlyList<string> iconNames, uint size, int scale = 1, CancellationToken cancellationToken = default);

		/// <summary>
		/// Resolves candidates in lookup order, including alternate sizes and inherited themes, so callers can skip images that fail to decode.
		/// </summary>
		Task<IReadOnlyList<IconLookupResult>> ResolveIconCandidatesAsync(IReadOnlyList<string> iconNames, uint size, int scale = 1, CancellationToken cancellationToken = default);
	}
}
