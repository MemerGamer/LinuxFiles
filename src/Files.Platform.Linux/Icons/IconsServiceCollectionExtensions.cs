// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Icons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Files.Platform.Linux.Icons
{
	/// <summary>
	/// Provides extension methods for registering the Linux icon theme services.
	/// </summary>
	public static class IconsServiceCollectionExtensions
	{
		/// <summary>
		/// Registers <see cref="IIconThemeProvider"/> backed by the freedesktop icon theme lookup.
		/// </summary>
		public static IServiceCollection AddLinuxIcons(this IServiceCollection services)
		{
			services.TryAddSingleton(_ => LinuxIconThemeOptions.FromEnvironment());
			services.TryAddSingleton<IIconThemeProvider, LinuxIconThemeProvider>();
			return services;
		}
	}
}
