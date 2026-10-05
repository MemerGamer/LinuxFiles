// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Storage;
using Files.App.Storage.Storables;
using Files.Core.Storage;
using Files.Core.Storage.Contracts;
using Files.Platform.Abstractions.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OwlCore.Storage;

namespace Files.Platform.Tests.Storables
{
	[TestClass]
	public sealed class FtpStorableRouteTests
	{
		[TestMethod]
		[DataRow("ftp://host", "host", 21, "/", "ftp://host")]
		[DataRow("ftp://host/", "host", 21, "/", "ftp://host")]
		[DataRow("FTP://Host:2121/a/b c", "Host", 2121, "/a/b c", "ftp://Host:2121/a/b c")]
		[DataRow("ftps://host/x", "host", 990, "/x", "ftps://host/x")]
		[DataRow("ftpes://host:21/x", "host", 21, "/x", "ftpes://host:21/x")]
		[DataRow("ftp://[::1]:2121/p", "::1", 2121, "/p", "ftp://[::1]:2121/p")]
		[DataRow("ftp://[2001:db8::1]/p", "2001:db8::1", 21, "/p", "ftp://[2001:db8::1]/p")]
		[DataRow("ftp://host/a\\b?c#d%20", "host", 21, "/a/b?c#d%20", "ftp://host/a/b?c#d%20")]
		public void Parse_MapsHostPortAndRawPath(string url, string host, int port, string path, string id)
		{
			var parsed = FtpUrl.Parse(url);

			Assert.AreEqual(host, parsed.Host);
			Assert.AreEqual(port, parsed.Port);
			Assert.AreEqual(path, parsed.Path);
			Assert.AreEqual(id, parsed.ToId());
		}

		[TestMethod]
		public void Parse_ExtractsAndDecodesCredentials_AndOmitsThemFromId()
		{
			var parsed = FtpUrl.Parse("ftp://us%40er:p%3Aw%2Fd@host:2121/dir");

			Assert.AreEqual("us@er", parsed.UserName);
			Assert.AreEqual("p:w/d", parsed.Password);
			Assert.AreEqual("host", parsed.Host);
			Assert.AreEqual("ftp://host:2121/dir", parsed.ToId());
			Assert.IsFalse(parsed.ToId().Contains("p%3Aw"));
		}

		[TestMethod]
		public void Parse_PasswordContainingAt_UsesLastAt()
		{
			var parsed = FtpUrl.Parse("ftp://user:p@ss@host/");

			Assert.AreEqual("p@ss", parsed.Password);
			Assert.AreEqual("host", parsed.Host);
		}

		[TestMethod]
		[DataRow("http://host/")]
		[DataRow("ftp://")]
		[DataRow("ftp:///path")]
		[DataRow("ftp://host:abc/")]
		[DataRow("ftp://host:99999/")]
		[DataRow("ftp://host:0/")]
		[DataRow("ftp://[::1/")]
		[DataRow("ftp://[host]/")]
		[DataRow("ftp://::1/")]
		[DataRow("/home/user")]
		[DataRow("")]
		public void TryParse_RejectsInvalid(string url)
		{
			Assert.IsFalse(FtpUrl.TryParse(url, out _));
		}

		[TestMethod]
		public void CanResolve_OnlyFtpSchemes()
		{
			var route = new FtpStorableRoute(new FakeFtpService());

			Assert.IsTrue(route.CanResolve("ftp://h/x"));
			Assert.IsTrue(route.CanResolve("FTPS://h"));
			Assert.IsTrue(route.CanResolve("ftpes://h"));
			Assert.IsFalse(route.CanResolve("/home/x"));
			Assert.IsFalse(route.CanResolve("http://h"));
			Assert.IsTrue(route.Order < LocalStorableRoute.DefaultOrder);
		}

		[TestMethod]
		public async Task TryGet_UsesStrippedId_AndFallsBackToFile()
		{
			var service = new FakeFtpService { FolderMissing = true };
			var route = new FtpStorableRoute(service);

			var result = await route.TryGetAsync("ftp://u:secret@host/dir/file.txt");

			Assert.IsNotNull(result);
			Assert.AreEqual("ftp://host/dir/file.txt", service.LastFileId);
			Assert.AreEqual("ftp://host/dir/file.txt", service.LastFolderId);
		}

		[TestMethod]
		public async Task TryGet_ReturnsNull_WhenNothingFoundOrServerFails()
		{
			Assert.IsNull(await new FtpStorableRoute(new FakeFtpService { FolderMissing = true, FileMissing = true }).TryGetAsync("ftp://h/x"));
			Assert.IsNull(await new FtpStorableRoute(new FakeFtpService { Fail = true }).TryGetAsync("ftp://h/x"));
			Assert.IsNull(await new FtpStorableRoute(new FakeFtpService()).TryGetAsync("not-ftp"));
		}

		[TestMethod]
		public async Task TryGet_PropagatesCancellation()
		{
			using var cts = new CancellationTokenSource();
			cts.Cancel();

			await Assert.ThrowsExactlyAsync<OperationCanceledException>(
				() => new FtpStorableRoute(new FakeFtpService()).TryGetAsync("ftp://h/x", cts.Token));
		}

		[TestMethod]
		public async Task Resolver_DispatchesFtpBeforeLocal()
		{
			var services = new ServiceCollection();
			services.AddSingleton<IFtpStorageService>(new FakeFtpService());
			services.AddSingleton<IStorableRoute, FtpStorableRoute>();
			services.AddStorables();
			using var provider = services.BuildServiceProvider();
			var resolver = provider.GetRequiredService<IStorableResolver>();

			Assert.IsTrue(resolver.CanResolve("ftp://h/x"));
			Assert.IsInstanceOfType<IFolder>(await resolver.TryGetAsync("ftp://h/x"));
		}

		[TestMethod]
		public void Credentials_PersistThroughSecretStore_ButUrlCredentialsStayInSession()
		{
			var store = new MemorySecretStore();
			var previous = FtpManager.SecretStore;
			FtpManager.SecretStore = store;
			try
			{
				var cache = new FtpCredentialCache();
				cache["Example.org"] = new NetworkCredential("bob", "pw");
				cache.SetSessionOnly("other.org", new NetworkCredential("eve", "typed"));

				Assert.AreEqual("pw", store.Get("Files FTP example.org", "bob"));
				Assert.IsNull(store.Get("Files FTP other.org", "eve"));

				// A fresh cache (new app session) reads the password back from the store
				var fresh = new FtpCredentialCache();
				Assert.IsTrue(fresh.TryGetValue("example.org", out var credential));
				Assert.AreEqual("bob", credential.UserName);
				Assert.AreEqual("pw", credential.Password);
				Assert.IsFalse(fresh.TryGetValue("other.org", out _));
			}
			finally
			{
				FtpManager.SecretStore = previous;
			}
		}

		private sealed class FakeFtpService : IFtpStorageService
		{
			public bool FolderMissing { get; init; }
			public bool FileMissing { get; init; }
			public bool Fail { get; init; }
			public string? LastFolderId { get; private set; }
			public string? LastFileId { get; private set; }

			public Task<IFolder> GetFolderAsync(string id, CancellationToken cancellationToken = default)
			{
				LastFolderId = id;
				if (Fail)
					throw new IOException("secret in message");
				if (FolderMissing)
					throw new DirectoryNotFoundException();
				return Task.FromResult<IFolder>(new FakeFolder(id));
			}

			public Task<IFile> GetFileAsync(string id, CancellationToken cancellationToken = default)
			{
				LastFileId = id;
				if (FileMissing)
					throw new FileNotFoundException();
				return Task.FromResult<IFile>(new FakeFile(id));
			}
		}

		private sealed class FakeFolder(string id) : IFolder
		{
			public string Id => id;
			public string Name => "f";
			public System.Collections.Generic.IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, CancellationToken cancellationToken = default)
				=> throw new NotSupportedException();
		}

		private sealed class FakeFile(string id) : IFile
		{
			public string Id => id;
			public string Name => "f";
			public Task<Stream> OpenStreamAsync(FileAccess accessMode = FileAccess.Read, CancellationToken cancellationToken = default)
				=> throw new NotSupportedException();
		}

		private sealed class MemorySecretStore : ISecretStore
		{
			private readonly System.Collections.Generic.Dictionary<(string, string), string> _items = new();

			public bool IsPersistent => false;
			public void Save(string resource, string account, string secret) => _items[(resource, account)] = secret;
			public string? Get(string resource, string account) => _items.TryGetValue((resource, account), out var v) ? v : null;
			public bool Delete(string resource, string account) => _items.Remove((resource, account));
		}
	}
}
