// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.Core.Storage.Contracts;
using OwlCore.Storage;

namespace Files.App.Storage.Archives
{
	public static class ArchiveServiceCollectionExtensions
	{
		/// <summary>Registers archive routing; the host provides IArchiveService and an optional IArchivePasswordPrompt.</summary>
		public static IServiceCollection AddArchiveStorables(this IServiceCollection services)
		{
			services.TryAddEnumerable(ServiceDescriptor.Singleton<IStorableRoute, ArchiveStorableRoute>());
			return services;
		}
	}
}
