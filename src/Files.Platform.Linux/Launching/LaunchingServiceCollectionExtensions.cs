// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Launching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Files.Platform.Linux.Launching
{
	/// <summary>
	/// Provides extension methods for registering the Linux launching services.
	/// </summary>
	public static class LaunchingServiceCollectionExtensions
	{
		/// <summary>
		/// Registers the launcher and templates services. Also requires <c>AddLinuxMime()</c>.
		/// </summary>
		public static IServiceCollection AddLinuxLaunching(this IServiceCollection services)
		{
			services.TryAddSingleton<IExecutableLocator, PathExecutableLocator>();
			services.TryAddSingleton<IProcessStarter, DetachedProcessStarter>();
			services.TryAddSingleton<TerminalResolver>();
			services.TryAddSingleton<ILauncherService, LinuxLauncherService>();
			services.TryAddSingleton<ITemplatesService, LinuxTemplatesService>();
			return services;
		}
	}
}
