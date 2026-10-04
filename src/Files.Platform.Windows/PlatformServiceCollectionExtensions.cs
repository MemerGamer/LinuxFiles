// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Files.Platform.Windows
{
	/// <summary>
	/// Provides extension methods for registering the Windows platform services.
	/// </summary>
	public static class PlatformServiceCollectionExtensions
	{
		/// <summary>
		/// Registers the Windows platform services.
		/// </summary>
		public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
		{
			return services.AddSingleton<IPlatformCapabilities, WindowsPlatformCapabilities>();
		}
	}
}
