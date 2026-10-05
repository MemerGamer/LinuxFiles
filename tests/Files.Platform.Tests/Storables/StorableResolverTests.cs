// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Storage.Storables;
using Files.Core.Storage.Contracts;
using Files.Core.Storage.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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

			Assert.AreEqual(archive.Result, result);
			Assert.AreEqual(0, local.Calls);
		}

		[TestMethod]
		public async Task TryGet_EqualOrderKeepsRegistrationOrder()
		{
			var first = new FakeRoute(order: 5, prefix: "/");
			var second = new FakeRoute(order: 5, prefix: "/");

			Assert.AreEqual(first.Result, await new StorableResolver([first, second]).TryGetAsync("/x"));
			Assert.AreEqual(second.Result, await new StorableResolver([second, first]).TryGetAsync("/x"));
		}

		[TestMethod]
		public async Task TryGet_NotMineFallsThroughToNextRoute()
		{
			// An archive route that sees a real directory named backup.zip declines it
			var archive = new FakeRoute(order: 10, prefix: "/home/u/backup.zip", status: StorableStatus.NotMine);
			var local = new FakeRoute(order: 100, prefix: "/");
			var resolver = new StorableResolver([local, archive]);

			var result = await resolver.TryGetAsync("/home/u/backup.zip/a");

			Assert.AreEqual(local.Result, result);
			Assert.AreEqual(1, archive.Calls);
		}

		[TestMethod]
		[DataRow(StorableStatus.NotFound)]
		[DataRow(StorableStatus.AccessDenied)]
		[DataRow(StorableStatus.Error)]
		public async Task TryGet_FailureFromClaimingRouteIsFinal(StorableStatus status)
		{
			var claiming = new FakeRoute(order: 1, prefix: "/", status: status);
			var fallback = new FakeRoute(order: 2, prefix: "/");
			var resolver = new StorableResolver([fallback, claiming]);

			var result = await resolver.TryGetAsync("/x");

			Assert.AreEqual(status, result.Status);
			Assert.IsNull(result.Item);
			Assert.AreEqual(0, fallback.Calls);
		}

		[TestMethod]
		public async Task TryGet_NoClaimingRoute_ReturnsNotMine()
		{
			var route = new FakeRoute(order: 1, prefix: "/");
			var resolver = new StorableResolver([route]);

			Assert.AreEqual(StorableStatus.NotMine, (await resolver.TryGetAsync("ftp://host/x")).Status);
			Assert.AreEqual(StorableStatus.NotMine, (await resolver.TryGetAsync(string.Empty)).Status);
			Assert.AreEqual(StorableStatus.NotMine, (await new StorableResolver([]).TryGetAsync("/x")).Status);
		}

		[TestMethod]
		public async Task TryGet_Canceled_Throws()
		{
			var resolver = new StorableResolver([new FakeRoute(order: 1, prefix: "/")]);

			await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => resolver.TryGetAsync("/x", new CancellationToken(canceled: true)));
		}

		[TestMethod]
		public void StorableResult_Invariants()
		{
			Assert.AreEqual(StorableStatus.NotMine, default(StorableResult).Status);
			Assert.AreEqual(StorableResult.NotMine, default);
			Assert.IsFalse(StorableResult.NotFound.IsSuccess);
			Assert.IsNull(StorableResult.AccessDenied.Item);
			Assert.ThrowsExactly<ArgumentNullException>(() => StorableResult.Success(null!));

			var success = StorableResult.Success(new MemoryFolder("id", "name"));
			Assert.IsTrue(success.IsSuccess);
			Assert.AreEqual(StorableStatus.Success, success.Status);
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
			Assert.AreEqual(archive.Result, await resolver.TryGetAsync("/archive.zip/entry"));
			Assert.IsTrue((await resolver.TryGetAsync("/")).IsSuccess);
		}

		private sealed class FakeRoute(int order, string prefix, StorableStatus status = StorableStatus.Success) : IStorableRoute
		{
			public StorableResult Result { get; } = status switch
			{
				StorableStatus.Success => StorableResult.Success(new MemoryFolder(prefix + "#" + Guid.NewGuid().ToString("N"), prefix)),
				StorableStatus.NotFound => StorableResult.NotFound,
				StorableStatus.AccessDenied => StorableResult.AccessDenied,
				StorableStatus.Error => StorableResult.Error,
				_ => StorableResult.NotMine,
			};

			public int Calls { get; private set; }

			public int Order => order;

			public Task<StorableResult> TryGetAsync(string path, CancellationToken cancellationToken = default)
			{
				if (!path.StartsWith(prefix, StringComparison.Ordinal))
					return Task.FromResult(StorableResult.NotMine);

				Calls++;
				return Task.FromResult(Result);
			}
		}
	}
}
