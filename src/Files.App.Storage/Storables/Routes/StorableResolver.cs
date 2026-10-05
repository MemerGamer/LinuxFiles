// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Core.Storage.Contracts;
using Files.Core.Storage.Enums;
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
		public async Task<StorableResult> TryGetAsync(string path, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (string.IsNullOrEmpty(path))
				return StorableResult.NotMine;

			foreach (var route in _routes)
			{
				var result = await route.TryGetAsync(path, cancellationToken).ConfigureAwait(false);
				if (result.Status is not StorableStatus.NotMine)
					return result;
			}

			return StorableResult.NotMine;
		}
	}
}
