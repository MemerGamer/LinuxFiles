// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Files.Platform.Abstractions.Mime
{
	/// <summary>
	/// Describes an installed application (a parsed Desktop Entry).
	/// </summary>
	/// <param name="Id">The desktop file ID, e.g. <c>org.gnome.gedit.desktop</c>.</param>
	/// <param name="Name">The localized application name.</param>
	/// <param name="Exec">The raw <c>Exec</c> value with field codes.</param>
	/// <param name="DesktopFilePath">The absolute path of the .desktop file.</param>
	/// <param name="IconName">The icon theme name or absolute path, if any.</param>
	/// <param name="GenericName">The localized generic name, if any.</param>
	/// <param name="Comment">The localized comment, if any.</param>
	/// <param name="RunInTerminal">True when the application must be run in a terminal.</param>
	/// <param name="NoDisplay">True when the application should not be shown in menus.</param>
	/// <param name="MimeTypes">The MIME types the application declares support for.</param>
	public sealed record DesktopApplication(
		string Id,
		string Name,
		string Exec,
		string DesktopFilePath,
		string? IconName = null,
		string? GenericName = null,
		string? Comment = null,
		bool RunInTerminal = false,
		bool NoDisplay = false,
		IReadOnlyList<string>? MimeTypes = null);
}
