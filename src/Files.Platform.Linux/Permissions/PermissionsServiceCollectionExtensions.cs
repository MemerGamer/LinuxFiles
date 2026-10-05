// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Permissions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Files.Platform.Linux.Permissions
{
	/// <summary>
	/// Provides extension methods for registering the Linux permission and stat services.
	/// </summary>
	public static class PermissionsServiceCollectionExtensions
	{
		/// <summary>
		/// Registers <see cref="IFilePermissionsService"/>, <see cref="IFileStatService"/> and <see cref="IFileAttributesService"/>.
		/// </summary>
		public static IServiceCollection AddLinuxPermissions(this IServiceCollection services)
		{
			services.TryAddSingleton<IFilePermissionsService, LinuxFilePermissionsService>();
			services.TryAddSingleton<IFileStatService, LinuxFileStatService>();
			services.TryAddSingleton<IFileAttributesService, LinuxFileAttributesService>();
			return services;
		}
	}
}
