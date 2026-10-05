// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Storage.Storables;
using Files.Core.Storage.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OwlCore.Storage;
using OwlCore.Storage.Memory;

namespace Files.Platform.Tests.Storables
{
	[TestClass]
	public sealed class StorableResolverTests
	{
		[TestMethod]
		public async Task TryGet_UsesLowestOrderClaimingRoute()
		{
			var local = new FakeRoute(order: 100, prefix: "/");
			var archive = new FakeRoute(order: 10, prefix: "/archive.zip");
			var resolver = new StorableResolver([local, archive]);

			var result = await resolver.TryGetAsync("/archive.zip/entry");

			Assert.AreSame(archive.Result, result);
			Assert.AreEqual(0, local.TryGetCalls);
		}

		[TestMethod]
		public async Task TryGet_EqualOrderKeepsRegistrationOrder()
		{
			var first = new FakeRoute(order: 5, prefix: "/");
			var second = new FakeRoute(order: 5, prefix: "/");

			Assert.AreSame(first.Result, await new StorableResolver([first, second]).TryGetAsync("/x"));
			Assert.AreSame(second.Result, await new StorableResolver([second, first]).TryGetAsync("/x"));
		}

		[TestMethod]
		public async Task TryGet_ClaimingRouteIsAuthoritative()
		{
			var claiming = new FakeRoute(order: 1, prefix: "/", returnsNull: true);
			var fallback = new FakeRoute(order: 2, prefix: "/");
			var resolver = new StorableResolver([fallback, claiming]);

			Assert.IsNull(await resolver.TryGetAsync("/x"));
			Assert.AreEqual(1, claiming.TryGetCalls);
			Assert.AreEqual(0, fallback.TryGetCalls);
		}

		[TestMethod]
		public async Task TryGet_NoClaimingRoute_ReturnsNull()
		{
			var route = new FakeRoute(order: 1, prefix: "/");
			var resolver = new StorableResolver([route]);

			Assert.IsNull(await resolver.TryGetAsync("ftp://host/x"));
			Assert.IsNull(await resolver.TryGetAsync(string.Empty));
			Assert.AreEqual(0, route.TryGetCalls);
		}

		[TestMethod]
		public void CanResolve_DoesNotResolve()
		{
			var route = new FakeRoute(order: 1, prefix: "/");
			var resolver = new StorableResolver([route]);

			Assert.IsTrue(resolver.CanResolve("/x"));
			Assert.IsFalse(resolver.CanResolve("relative"));
			Assert.IsFalse(resolver.CanResolve(string.Empty));
			Assert.AreEqual(0, route.TryGetCalls);
		}

		[TestMethod]
		public async Task TryGet_Canceled_Throws()
		{
			var resolver = new StorableResolver([new FakeRoute(order: 1, prefix: "/")]);

			await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => resolver.TryGetAsync("/x", new CancellationToken(canceled: true)));
		}

		[TestMethod]
		public async Task AddStorables_RegistersLocalRouteOnceAndHonorsExtraRoutes()
		{
			var archive = new FakeRoute(order: LocalStorableRoute.DefaultOrder - 1, prefix: "/archive.zip");
			var services = new ServiceCollection()
				.AddStorables()
				.AddStorables()
				.AddSingleton<IStorableRoute>(archive);
			using var provider = services.BuildServiceProvider();

			var routes = provider.GetServices<IStorableRoute>().ToArray();
			var resolver = provider.GetRequiredService<IStorableResolver>();

			Assert.AreEqual(1, routes.OfType<LocalStorableRoute>().Count());
			Assert.AreSame(resolver, provider.GetRequiredService<IStorableResolver>());
			Assert.AreSame(archive.Result, await resolver.TryGetAsync("/archive.zip/entry"));
			Assert.IsTrue(resolver.CanResolve("/"));
		}

		private sealed class FakeRoute(int order, string prefix, bool returnsNull = false) : IStorableRoute
		{
			public IStorable? Result { get; } = returnsNull ? null : new MemoryFolder(prefix + "#" + Guid.NewGuid().ToString("N"), prefix);

			public int TryGetCalls { get; private set; }

			public int Order => order;

			public bool CanResolve(string path) => path.StartsWith(prefix, StringComparison.Ordinal);

			public Task<IStorable?> TryGetAsync(string path, CancellationToken cancellationToken = default)
			{
				TryGetCalls++;
				return Task.FromResult(Result);
			}
		}
	}
}
