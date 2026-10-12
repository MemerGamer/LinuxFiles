// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.ApplicationModel.Resources;

namespace Files.App.Helpers
{
	/// <summary>
	/// Looks up localized strings from the app's Resources.resw (MRT on Windows, Uno's ResourceLoader elsewhere).
	/// </summary>
	internal static class ResourceLookup
	{
		private static readonly ResourceLoader _loader = ResourceLoader.GetForViewIndependentUse();

		public static string? TryGet(string key)
		{
			var value = _loader.GetString(key);
			return string.IsNullOrEmpty(value) ? null : value;
		}

		public static void Reset() { }
	}
}
