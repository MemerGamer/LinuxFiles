// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Files.Platform.Linux
{
	/// <summary>
	/// Provides extension methods for registering the Linux app data services.
	/// </summary>
	public static class AppDataServiceCollectionExtensions
	{
		/// <summary>
		/// Registers app data paths, user directories and the local settings store.
		/// </summary>
		public static IServiceCollection AddLinuxAppData(this IServiceCollection services)
		{
			return services
				.AddSingleton<IAppDataPaths, LinuxAppDataPaths>()
				.AddSingleton<IUserDirectories, LinuxUserDirectories>()
				.AddSingleton<ILocalSettingsStore>(sp => new LinuxLocalSettingsStore(sp.GetRequiredService<IAppDataPaths>()));
		}
	}
}
