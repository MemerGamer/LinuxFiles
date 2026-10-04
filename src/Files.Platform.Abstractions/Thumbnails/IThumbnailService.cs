// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Thumbnails
{
	/// <summary>
	/// Options that control how a thumbnail is retrieved.
	/// </summary>
	[Flags]
	public enum ThumbnailOptions
	{
		/// <summary>
		/// Default behavior: use the cache and generate the thumbnail when missing.
		/// </summary>
		None = 0,

		/// <summary>
		/// Return a thumbnail only if it is already cached; never generate one.
		/// </summary>
		ReturnOnlyIfCached = 1,

		/// <summary>
		/// Ignore any cached thumbnail and regenerate it.
		/// </summary>
		ForceRegenerate = 2,
	}

	/// <summary>
	/// Provides content thumbnails (image previews) for files.
	/// </summary>
	public interface IThumbnailService
	{
		/// <summary>
		/// Gets a PNG-encoded thumbnail from the size bucket that covers <paramref name="requestedSize"/>, or <see langword="null"/> when none is available.
		/// </summary>
		/// <param name="path">The absolute path of the file.</param>
		/// <param name="requestedSize">The desired size in physical pixels.</param>
		/// <param name="options">Retrieval options.</param>
		/// <param name="cancellationToken">A token to cancel the operation.</param>
		Task<byte[]?> GetThumbnailAsync(string path, uint requestedSize, ThumbnailOptions options = ThumbnailOptions.None, CancellationToken cancellationToken = default);
	}
}
