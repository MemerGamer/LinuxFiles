// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Abstractions
{
	/// <summary>
	/// Describes which features are available on the current platform.
	/// </summary>
	public interface IPlatformCapabilities
	{
		/// <summary>
		/// Gets the kind of platform the app is running on.
		/// </summary>
		PlatformKind Kind { get; }

		/// <summary>
		/// Gets a value indicating whether third-party shell context menu extensions are supported.
		/// </summary>
		bool SupportsShellContextMenuExtensions { get; }

		/// <summary>
		/// Gets a value indicating whether shell preview handlers are supported.
		/// </summary>
		bool SupportsPreviewHandlers { get; }

		/// <summary>
		/// Gets a value indicating whether Windows libraries are supported.
		/// </summary>
		bool SupportsLibraries { get; }

		/// <summary>
		/// Gets a value indicating whether ACL-based security descriptors are supported.
		/// </summary>
		bool SupportsAclSecurity { get; }

		/// <summary>
		/// Gets a value indicating whether digital signature inspection is supported.
		/// </summary>
		bool SupportsDigitalSignatures { get; }

		/// <summary>
		/// Gets a value indicating whether executable compatibility properties are supported.
		/// </summary>
		bool SupportsCompatibilityProperties { get; }

		/// <summary>
		/// Gets a value indicating whether taskbar jump lists are supported.
		/// </summary>
		bool SupportsJumpLists { get; }

		/// <summary>
		/// Gets a value indicating whether system backdrop materials (Mica/Acrylic) are supported.
		/// </summary>
		bool SupportsSystemBackdrop { get; }

		/// <summary>
		/// Gets a value indicating whether a custom title bar is supported.
		/// </summary>
		bool SupportsCustomTitleBar { get; }

		/// <summary>
		/// Gets a value indicating whether tearing tabs out into new windows is supported.
		/// </summary>
		bool SupportsTabTearOut { get; }

		/// <summary>
		/// Gets a value indicating whether Windows Subsystem for Linux integration is supported.
		/// </summary>
		bool SupportsWsl { get; }

		/// <summary>
		/// Gets a value indicating whether formatting drives from the app is supported.
		/// </summary>
		bool SupportsDriveFormatting { get; }

		/// <summary>
		/// Gets a value indicating whether shortcut files (.lnk/.url) are supported.
		/// </summary>
		bool SupportsShortcutFiles { get; }
	}
}
