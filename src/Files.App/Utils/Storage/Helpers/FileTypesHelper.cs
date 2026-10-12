// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Resolves the localized shell type name for a file extension (for example ".txt" produces "Text Document").
	/// </summary>
	public static class FileTypesHelper
	{
		// The type name is identical for every file of an extension, so cache it by extension.
		private static readonly ConcurrentDictionary<string, string> typeNameCache = new(StringComparer.OrdinalIgnoreCase);

		public static unsafe string GetLocalizedTypeName(string? extension)
		{
			if (string.IsNullOrEmpty(extension))
				return string.Empty;

			// LINUX-TODO(filetype): use IMimeTypeService.GetDescriptionAsync (shared-mime-info) for localized type names
			if (!OperatingSystem.IsWindows())
				return string.Empty;

			return string.Empty;
		}
	}
}
