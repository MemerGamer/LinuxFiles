// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Wallpaper
{
	/// <summary>
	/// Where an image is applied.
	/// </summary>
	public enum WallpaperTarget
	{
		/// <summary>The desktop background.</summary>
		Background,

		/// <summary>The lock screen.</summary>
		LockScreen,
	}

	/// <summary>
	/// The outcome of a wallpaper request.
	/// </summary>
	public enum WallpaperResult
	{
		/// <summary>The desktop applied the image.</summary>
		Applied,

		/// <summary>The user cancelled the desktop's confirmation.</summary>
		Cancelled,

		/// <summary>The desktop could not apply the image.</summary>
		Failed,

		/// <summary>No wallpaper service is available.</summary>
		Unavailable,
	}

	/// <summary>
	/// Sets the desktop wallpaper through the desktop's own confirmation UI.
	/// </summary>
	public interface IWallpaperService
	{
		/// <summary>
		/// Gets whether the desktop offers a wallpaper service. Never changes anything.
		/// </summary>
		Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// Asks the desktop to apply <paramref name="imagePath"/> to <paramref name="target"/>.
		/// </summary>
		Task<WallpaperResult> SetAsync(string imagePath, WallpaperTarget target, CancellationToken cancellationToken = default);
	}
}
