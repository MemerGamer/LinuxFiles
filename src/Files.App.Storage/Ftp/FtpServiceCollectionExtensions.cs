// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Core.Storage.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Files.App.Storage
{
	public static class FtpServiceCollectionExtensions
	{
		/// <summary>
		/// Registers <see cref="IFtpStorageService"/> and the ftp:// / ftps:// <see cref="IStorableRoute"/>.
		/// </summary>
		public static IServiceCollection AddFtpStorables(this IServiceCollection services)
		{
			services.TryAddSingleton<IFtpStorageService, FtpStorageService>();
			services.TryAddEnumerable(ServiceDescriptor.Singleton<IStorableRoute, FtpStorableRoute>());

			return services;
		}
	}
}
