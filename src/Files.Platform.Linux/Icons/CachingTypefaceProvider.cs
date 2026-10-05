// Copyright (c) Files Community
// Licensed under the MIT License.

using SkiaSharp;
using Svg.Skia.TypefaceProviders;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Files.Platform.Linux.Icons
{
	/// <summary>
	/// Memoizes typeface lookups for SVG rendering. Svg.Skia resolves a typeface for every paint it converts, and the default
	/// font-manager provider enumerates fontconfig style sets each time, which is very slow with thousands of installed fonts.
	/// </summary>
	internal sealed class CachingTypefaceProvider : ITypefaceProvider
	{
		private static readonly ConcurrentDictionary<(string Family, SKFontStyleWeight Weight, SKFontStyleWidth Width, SKFontStyleSlant Slant), SKTypeface?> _cache = new();

		private readonly IReadOnlyList<ITypefaceProvider> _providers;

		public CachingTypefaceProvider(IEnumerable<ITypefaceProvider>? providers)
		{
			_providers = [.. providers ?? [new FontManagerTypefaceProvider(), new DefaultTypefaceProvider()]];
		}

		public SKTypeface? FromFamilyName(string fontFamily, SKFontStyleWeight fontWeight, SKFontStyleWidth fontWidth, SKFontStyleSlant fontStyle)
		{
			return _cache.GetOrAdd((fontFamily ?? string.Empty, fontWeight, fontWidth, fontStyle), key =>
			{
				foreach (var provider in _providers)
				{
					if (provider.FromFamilyName(key.Family, key.Weight, key.Width, key.Slant) is { } typeface)
						return typeface;
				}

				return null;
			});
		}
	}
}
