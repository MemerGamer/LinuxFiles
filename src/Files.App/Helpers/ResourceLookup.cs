// Copyright (c) Files Community
// Licensed under the MIT License.

#if WINDOWS
using Microsoft.Windows.ApplicationModel.Resources;
#else
using Windows.ApplicationModel.Resources;
#endif

namespace Files.App.Helpers
{
	/// <summary>
	/// Looks up localized strings from the app's Resources.resw (MRT on Windows, Uno's ResourceLoader elsewhere).
	/// </summary>
	internal static class ResourceLookup
	{
#if WINDOWS
		private static readonly ResourceMap? _tree = new ResourceManager().MainResourceMap.TryGetSubtree("Resources");

		public static string? TryGet(string key) => _tree?.TryGetValue(key)?.ValueAsString;
#else
		private static readonly ResourceLoader _loader = ResourceLoader.GetForViewIndependentUse();

		public static string? TryGet(string key)
		{
			var value = _loader.GetString(key);
			return string.IsNullOrEmpty(value) ? null : value;
		}
#endif
	}
}
