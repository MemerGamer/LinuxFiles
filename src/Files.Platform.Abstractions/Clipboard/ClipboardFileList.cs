// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Files.Platform.Abstractions.Clipboard
{
	/// <summary>
	/// A list of file system paths together with the operation requested on paste.
	/// </summary>
	/// <param name="Paths">Absolute local paths.</param>
	/// <param name="Operation">Whether the paste copies or moves the files.</param>
	public sealed record ClipboardFileList(IReadOnlyList<string> Paths, ClipboardOperation Operation);
}
