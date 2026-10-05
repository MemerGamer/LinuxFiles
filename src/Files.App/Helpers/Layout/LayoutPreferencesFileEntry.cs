// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Helpers
{
	/// <summary>
	/// A folder's layout preferences as stored by the Linux database, keyed by path with the folder's <c>dev:ino</c> identity
	/// so the preferences follow the folder when it is renamed or moved.
	/// </summary>
	public sealed class LayoutPreferencesFileEntry
	{
		public string FilePath { get; set; } = string.Empty;

		public string? FileId { get; set; }

		public LayoutPreferencesItem Preferences { get; set; } = new();
	}
}
