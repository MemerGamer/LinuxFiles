// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Elevation;
using Files.Platform.Abstractions.Instance;
using Files.Platform.Abstractions.Notifications;
using Files.Platform.Abstractions.Recent;
using Files.Platform.Abstractions.Tags;
using Files.Platform.Linux.DBus;
using Files.Platform.Linux.Elevation;
using Files.Platform.Linux.Instance;
using Files.Platform.Linux.Launching;
using Files.Platform.Linux.Mime;
using Files.Platform.Linux.Notifications;
using Files.Platform.Linux.Recent;
using Files.Platform.Linux.Tags;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Files.Platform.Linux
{
	/// <summary>
	/// Registers the desktop integration services: single instance, FileManager1, recent files, file tags, notifications and elevation.
	/// </summary>
	public static class SystemIntegrationServiceCollectionExtensions
	{
		/// <summary>
		/// Registers the services. A <see cref="LinuxSingleInstanceService"/> registered earlier (the one the process entry point created) is reused.
		/// </summary>
		public static IServiceCollection AddLinuxSystemIntegration(this IServiceCollection services)
		{
			services.TryAddSingleton<LinuxSingleInstanceService>(_ => new LinuxSingleInstanceService());
			services.TryAddSingleton<ISingleInstanceService>(sp => sp.GetRequiredService<LinuxSingleInstanceService>());
			services.TryAddSingleton(sp => new FileManagerService(null, sp.GetRequiredService<LinuxSingleInstanceService>().Post));
			services.TryAddSingleton<IFileTagsStore, XattrFileTagsStore>();
			services.TryAddSingleton<IRecentFilesStore>(_ => new XbelRecentFilesStore(new XdgDirectories(System.Environment.GetEnvironmentVariable).DataHome));
			services.TryAddSingleton<INotificationService>(_ => new DBusNotificationService());
			services.TryAddSingleton<IElevatedProcessRunner, ProcessElevatedRunner>();
			services.TryAddSingleton<IExecutableLocator, PathExecutableLocator>();
			services.TryAddSingleton<IElevationService, PkexecElevationService>();
			return services;
		}
	}
}
