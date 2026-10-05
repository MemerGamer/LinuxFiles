// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Abstractions.Clipboard
{
	/// <summary>
	/// The operation a file list on the clipboard asks the pasting application to perform.
	/// </summary>
	public enum ClipboardOperation
	{
		/// <summary>The files are copied on paste.</summary>
		Copy,

		/// <summary>The files are moved on paste.</summary>
		Cut,
	}
}
