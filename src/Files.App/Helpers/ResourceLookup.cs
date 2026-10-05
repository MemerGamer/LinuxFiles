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
		private static readonly ResourceManager _manager = new();
		private static readonly ResourceMap? _tree = _manager.MainResourceMap.TryGetSubtree("Resources");
		private static ResourceContext? _context;

		public static string? TryGet(string key)
		{
			_context ??= _manager.CreateResourceContext();
			return _tree?.TryGetValue(key, _context)?.ValueAsString;
		}

		/// <summary>Drops the cached context so a changed language override is picked up.</summary>
		public static void Reset() => _context = null;
#else
		private static readonly ResourceLoader _loader = ResourceLoader.GetForViewIndependentUse();

		public static string? TryGet(string key)
		{
			var value = _loader.GetString(key);
			return string.IsNullOrEmpty(value) ? null : value;
		}

		public static void Reset() { }
#endif
	}
}
