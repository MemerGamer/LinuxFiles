// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Mime;
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
			// External thumbnailers (video, PDF, ...) are picked by MIME type, so resolve it through IMimeTypeService when registered
			services.TryAddSingleton(provider =>
			{
				var options = new LinuxThumbnailOptions();
				if (provider.GetService<IMimeTypeService>() is { } mime)
				{
					// Called from the service's background generation path, never from the UI thread
					options.MimeTypeResolver = path => mime.GetMimeTypeAsync(path).GetAwaiter().GetResult();
				}

				return options;
			});
			services.TryAddSingleton<IThumbnailService, LinuxThumbnailService>();
			return services;
		}
	}
}
