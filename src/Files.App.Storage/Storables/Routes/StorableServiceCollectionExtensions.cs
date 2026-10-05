// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Core.Storage.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Files.App.Storage.Storables
{
	/// <summary>
	/// Provides extension methods for registering storable path resolution.
	/// </summary>
	public static class StorableServiceCollectionExtensions
	{
		/// <summary>
		/// Registers <see cref="IStorableResolver"/> and the built-in <see cref="IStorableRoute"/>s.
		/// </summary>
		/// <remarks>
		/// Additional routes are registered with <c>services.AddSingleton&lt;IStorableRoute, TRoute&gt;()</c>;
		/// the resolver orders them by <see cref="IStorableRoute.Order"/>.
		/// </remarks>
		/// <param name="services">The service collection.</param>
		/// <returns>The same service collection, for chaining.</returns>
		public static IServiceCollection AddStorables(this IServiceCollection services)
		{
			services.TryAddEnumerable(ServiceDescriptor.Singleton<IStorableRoute, LocalStorableRoute>());
			services.TryAddSingleton<IStorableResolver, StorableResolver>();

			return services;
		}
	}
}
