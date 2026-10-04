// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Trash;
using Microsoft.Extensions.DependencyInjection;

namespace Files.Platform.Linux.Trash
{
	/// <summary>
	/// Provides extension methods for registering the Linux trash service.
	/// </summary>
	public static class TrashServiceCollectionExtensions
	{
		/// <summary>
		/// Registers the FreeDesktop.org trash implementation of <see cref="ITrashService"/>.
		/// </summary>
		public static IServiceCollection AddLinuxTrash(this IServiceCollection services)
		{
			return services.AddSingleton<ITrashService>(_ => new LinuxTrashService());
		}
	}
}
