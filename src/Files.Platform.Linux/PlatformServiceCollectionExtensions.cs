// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions;
using Files.Platform.Linux.Archives;
using Files.Platform.Linux.Enumeration;
using Files.Platform.Linux.FileOperations;
using Files.Platform.Linux.Icons;
using Files.Platform.Linux.Launching;
using Files.Platform.Linux.Mime;
using Files.Platform.Linux.Thumbnails;
using Files.Platform.Linux.Trash;
using Files.Platform.Linux.Volumes;
using Files.Platform.Linux.Watching;
using Microsoft.Extensions.DependencyInjection;

namespace Files.Platform.Linux
{
	/// <summary>
	/// Provides extension methods for registering the Linux platform services.
	/// </summary>
	public static class PlatformServiceCollectionExtensions
	{
		/// <summary>
		/// Registers the Linux platform services.
		/// </summary>
		public static IServiceCollection AddLinuxPlatform(this IServiceCollection services)
		{
			return services
				.AddSingleton<IPlatformCapabilities, LinuxPlatformCapabilities>()
				.AddLinuxAppData()
				.AddLinuxEnumeration()
				.AddLinuxWatching()
				.AddLinuxThumbnails()
				.AddLinuxIcons()
				.AddLinuxTrash()
				.AddLinuxMime()
				.AddLinuxLaunching()
				.AddLinuxFileOperations()
				.AddLinuxArchives()
				.AddLinuxVolumes();
		}
	}
}
