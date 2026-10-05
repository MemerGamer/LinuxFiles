// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Core.Storage.Contracts;
using OwlCore.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.Storage.Storables
{
	/// <inheritdoc cref="IStorableResolver"/>
	public sealed class StorableResolver : IStorableResolver
	{
		private readonly IStorableRoute[] _routes;

		/// <summary>
		/// Initializes a new instance of the <see cref="StorableResolver"/> class.
		/// </summary>
		/// <param name="routes">The routes to dispatch to, in registration order.</param>
		public StorableResolver(IEnumerable<IStorableRoute> routes)
		{
			ArgumentNullException.ThrowIfNull(routes);

			// OrderBy is stable, so equal orders keep their registration order
			_routes = routes.OrderBy(static route => route.Order).ToArray();
		}

		/// <inheritdoc/>
		public bool CanResolve(string path)
		{
			return FindRoute(path) is not null;
		}

		/// <inheritdoc/>
		public Task<IStorable?> TryGetAsync(string path, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return FindRoute(path) is { } route
				? route.TryGetAsync(path, cancellationToken)
				: Task.FromResult<IStorable?>(null);
		}

		private IStorableRoute? FindRoute(string path)
		{
			if (string.IsNullOrEmpty(path))
				return null;

			foreach (var route in _routes)
			{
				if (route.CanResolve(path))
					return route;
			}

			return null;
		}
	}
}
