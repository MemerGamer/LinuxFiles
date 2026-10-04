// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions;

namespace Files.Platform.Windows
{
	/// <summary>
	/// Describes the features available on Windows.
	/// </summary>
	public sealed class WindowsPlatformCapabilities : IPlatformCapabilities
	{
		/// <inheritdoc/>
		public PlatformKind Kind => PlatformKind.Windows;

		/// <inheritdoc/>
		public bool SupportsShellContextMenuExtensions => true;

		/// <inheritdoc/>
		public bool SupportsPreviewHandlers => true;

		/// <inheritdoc/>
		public bool SupportsLibraries => true;

		/// <inheritdoc/>
		public bool SupportsAclSecurity => true;

		/// <inheritdoc/>
		public bool SupportsDigitalSignatures => true;

		/// <inheritdoc/>
		public bool SupportsCompatibilityProperties => true;

		/// <inheritdoc/>
		public bool SupportsJumpLists => true;

		/// <inheritdoc/>
		public bool SupportsSystemBackdrop => true;

		/// <inheritdoc/>
		public bool SupportsCustomTitleBar => true;

		/// <inheritdoc/>
		public bool SupportsTabTearOut => true;

		/// <inheritdoc/>
		public bool SupportsWsl => true;

		/// <inheritdoc/>
		public bool SupportsDriveFormatting => true;

		/// <inheritdoc/>
		public bool SupportsShortcutFiles => true;
	}
}
