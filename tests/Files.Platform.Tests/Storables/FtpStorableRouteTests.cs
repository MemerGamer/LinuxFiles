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
		public void Credentials_PersistThroughSecretStore_ScopedBySchemeHostAndPort()
		{
			var store = new MemorySecretStore();
			var previous = FtpManager.SecretStore;
			FtpManager.SecretStore = store;
			try
			{
				var key = FtpUrl.Parse("ftps://Example.org/x").GetCredentialKey();
				var cache = new FtpCredentialCache();
				cache[key] = new NetworkCredential("bob", "pw");

				Assert.AreEqual("ftps://example.org:990", key);
				Assert.AreEqual("pw", store.Get("Files FTP ftps://example.org:990", "bob"));

				var fresh = new FtpCredentialCache();
				Assert.IsTrue(fresh.TryGetValue(key, out var credential));
				Assert.AreEqual("pw", credential.Password);

				// Other scheme or port never sees it
				Assert.IsFalse(fresh.TryGetValue(FtpUrl.Parse("ftp://example.org").GetCredentialKey(), out _));
				Assert.IsFalse(fresh.TryGetValue(FtpUrl.Parse("ftps://example.org:2121").GetCredentialKey(), out _));
				Assert.IsFalse(fresh.TryGetValue("example.org", out _));
			}
			finally
			{
				FtpManager.SecretStore = previous;
			}
		}

		[TestMethod]
		public void UrlCredentials_AreNeverPersisted_AndNeverOverrideSaved()
		{
			var store = new MemorySecretStore();
			var previous = FtpManager.SecretStore;
			FtpManager.SecretStore = store;
			try
			{
				var key = FtpUrl.Parse("ftp://h").GetCredentialKey();
				var cache = new FtpCredentialCache();

				cache.SetFromUrl(key, new NetworkCredential("eve", "typed"));
				Assert.IsTrue(cache.TryGetValue(key, out var fromUrl));
				Assert.AreEqual("eve", fromUrl.UserName);
				Assert.IsFalse(new FtpCredentialCache().TryGetValue(key, out _));
				Assert.IsFalse(cache.TryGetValue(FtpUrl.Parse("ftp://other").GetCredentialKey(), out _));

				cache[key] = new NetworkCredential("bob", "saved");
				cache.SetFromUrl(key, new NetworkCredential("eve", "typed"));
				Assert.IsTrue(cache.TryGetValue(key, out var chosen));
				Assert.AreEqual("bob", chosen.UserName);
				Assert.IsNull(store.Get("Files FTP ftp://h:21", "eve"));
			}
			finally
			{
				FtpManager.SecretStore = previous;
			}
		}

		[TestMethod]
		public void LegacyHostKeys_AreSessionOnly()
		{
			var store = new MemorySecretStore();
			var previous = FtpManager.SecretStore;
			FtpManager.SecretStore = store;
			try
			{
				var cache = new FtpCredentialCache();
				cache["host"] = new NetworkCredential("bob", "pw");

				Assert.IsNull(store.Get("Files FTP host", "bob"));
				Assert.IsTrue(cache.TryGetValue("host", out _));
			}
			finally
			{
				FtpManager.SecretStore = previous;
			}
		}

		[TestMethod]
		public void ToString_NeverContainsUserInfo()
		{
			var url = FtpUrl.Parse("ftp://user:secret@host/dir");

			Assert.IsFalse(url.ToString().Contains("secret"));
			Assert.IsFalse(url.ToString().Contains("user"));
		}

		[TestMethod]
		public void RequiresTls_NeverFallsBackToCleartextForPasswords()
		{
			Assert.IsTrue(FtpManager.RequiresTls(FtpUrl.Parse("ftps://tls1.example"), anonymous: true));
			Assert.IsTrue(FtpManager.RequiresTls(FtpUrl.Parse("ftpes://tls1.example"), anonymous: true));
			Assert.IsTrue(FtpManager.RequiresTls(FtpUrl.Parse("ftp://tls1.example"), anonymous: false));
			Assert.IsFalse(FtpManager.RequiresTls(FtpUrl.Parse("ftp://tls1.example"), anonymous: true));

			FtpManager.ApproveCleartext(FtpUrl.Parse("ftp://tls2.example").GetCredentialKey());
			Assert.IsFalse(FtpManager.RequiresTls(FtpUrl.Parse("ftp://tls2.example"), anonymous: false));
			Assert.IsTrue(FtpManager.RequiresTls(FtpUrl.Parse("ftps://tls2.example"), anonymous: false));
		}

		[TestMethod]
		public void CleartextApproval_IsPerSchemeHostAndPort()
		{
			var key = FtpUrl.Parse("ftp://approve.example:2121").GetCredentialKey();
			Assert.IsFalse(FtpManager.IsCleartextApproved(key));

			FtpManager.ApproveCleartext(key);

			Assert.IsTrue(FtpManager.IsCleartextApproved(key));
			Assert.IsFalse(FtpManager.IsCleartextApproved(FtpUrl.Parse("ftp://approve.example").GetCredentialKey()));
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
