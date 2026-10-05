// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Fonts
{
	/// <summary>
	/// The outcome of installing a font for the current user.
	/// </summary>
	public enum FontInstallResult
	{
		/// <summary>The font was installed.</summary>
		Installed,

		/// <summary>A font with the same file name is already installed and overwriting was not allowed.</summary>
		AlreadyExists,

		/// <summary>The file is not a recognized font file.</summary>
		NotAFont,

		/// <summary>The font could not be installed.</summary>
		Failed,
	}

	/// <summary>
	/// Installs fonts for the current user only.
	/// </summary>
	public interface IFontInstallService
	{
		/// <summary>
		/// Installs the font file at <paramref name="sourcePath"/> for the current user. An existing font with the same name is replaced only if <paramref name="overwrite"/> is set.
		/// </summary>
		Task<FontInstallResult> InstallAsync(string sourcePath, bool overwrite, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets whether a font with the same file name as <paramref name="sourcePath"/> is already installed for the current user.
		/// </summary>
		bool IsInstalled(string sourcePath);
	}
}
