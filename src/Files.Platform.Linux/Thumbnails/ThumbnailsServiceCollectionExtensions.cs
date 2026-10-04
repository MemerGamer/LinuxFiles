// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Thumbnails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Files.Platform.Linux.Thumbnails
{
	/// <summary>
	/// Provides extension methods for registering the Linux thumbnail services.
	/// </summary>
	public static class ThumbnailsServiceCollectionExtensions
	{
		/// <summary>
		/// Registers <see cref="IThumbnailService"/> backed by the XDG thumbnail cache.
		/// </summary>
		public static IServiceCollection AddLinuxThumbnails(this IServiceCollection services)
		{
			services.TryAddSingleton<LinuxThumbnailOptions>();
			services.TryAddSingleton<IThumbnailService, LinuxThumbnailService>();
			return services;
		}
	}
}
