// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Mime
{
	/// <summary>
	/// Resolves MIME types and their user-visible metadata.
	/// </summary>
	public interface IMimeTypeService
	{
		/// <summary>
		/// Gets the MIME type of a path, e.g. <c>inode/directory</c>, <c>text/plain</c> or <c>application/octet-stream</c>.
		/// </summary>
		Task<string> GetMimeTypeAsync(string path, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets the localized description of a MIME type (e.g. "PNG image"), or null when unknown.
		/// </summary>
		Task<string?> GetDescriptionAsync(string mimeType, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets the icon theme name for a MIME type (e.g. <c>image-png</c>).
		/// </summary>
		Task<string> GetIconNameAsync(string mimeType, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets the generic fallback icon theme name for a MIME type (e.g. <c>image-x-generic</c>).
		/// </summary>
		Task<string> GetGenericIconNameAsync(string mimeType, CancellationToken cancellationToken = default);
	}
}
