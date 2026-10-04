// Copyright (c) Files Community
// Licensed under the MIT License.

#pragma warning disable CA1416

using Files.Platform.Abstractions.Instance;
using Files.Platform.Linux.Instance;
using Files.Platform.Linux.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Files.Platform.Tests.SystemIntegration
{
	[TestClass]
	public sealed class SingleInstanceSecurityTests
	{
		private sealed class FakeInspector : IFileOwnershipInspector
		{
			public Dictionary<string, FileEntryInfo> Entries { get; } = [];

			public bool TryGetInfo(string path, out FileEntryInfo info)
			{
				if (Entries.TryGetValue(path, out var found))
				{
					info = found;
					return true;
				}

				info = null!;
				return false;
			}
		}

		private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

		[TestMethod]
		public void DirectoryMustBeOwnedPrivateAndNotASymlink()
		{
			var fake = new FakeInspector();
			fake.Entries["/run/user/1000"] = new(true, false, 1000, Private);
			fake.Entries["/run/user/other"] = new(true, false, 4242, Private);
			fake.Entries["/run/user/open"] = new(true, false, 1000, Private | UnixFileMode.GroupRead);
			fake.Entries["/run/user/sticky-tmp"] = new(true, false, 1000, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherWrite);
			fake.Entries["/run/user/link"] = new(true, true, 1000, Private);
			fake.Entries["/run/user/file"] = new(false, false, 1000, Private);

			Assert.IsTrue(SingleInstanceSecurity.IsTrustedDirectory("/run/user/1000", fake, 1000));
			Assert.IsFalse(SingleInstanceSecurity.IsTrustedDirectory("/run/user/other", fake, 1000), "owned by someone else");
			Assert.IsFalse(SingleInstanceSecurity.IsTrustedDirectory("/run/user/open", fake, 1000), "group readable");
			Assert.IsFalse(SingleInstanceSecurity.IsTrustedDirectory("/run/user/sticky-tmp", fake, 1000), "world writable");
			Assert.IsFalse(SingleInstanceSecurity.IsTrustedDirectory("/run/user/link", fake, 1000), "symlink");
			Assert.IsFalse(SingleInstanceSecurity.IsTrustedDirectory("/run/user/file", fake, 1000), "not a directory");
			Assert.IsFalse(SingleInstanceSecurity.IsTrustedDirectory("/run/user/missing", fake, 1000));
		}

		[TestMethod]
		public void EntriesMustNotBeSymlinksOrForeign()
		{
			var fake = new FakeInspector();
			fake.Entries["/d/lock"] = new(false, false, 1000, UnixFileMode.UserRead);
			fake.Entries["/d/link"] = new(false, true, 1000, UnixFileMode.UserRead);
			fake.Entries["/d/foreign"] = new(false, false, 0, UnixFileMode.UserRead);
			fake.Entries["/d/dir"] = new(true, false, 1000, Private);

			Assert.IsTrue(SingleInstanceSecurity.IsSafeEntry("/d/lock", fake, 1000));
			Assert.IsFalse(SingleInstanceSecurity.IsSafeEntry("/d/link", fake, 1000));
			Assert.IsFalse(SingleInstanceSecurity.IsSafeEntry("/d/foreign", fake, 1000));
			Assert.IsFalse(SingleInstanceSecurity.IsSafeEntry("/d/dir", fake, 1000));
			Assert.IsTrue(SingleInstanceSecurity.IsSafeEntry("/nonexistent-for-test/x", fake, 1000));
		}

		private static string NewPrivateDir()
		{
			var dir = Path.Combine(Path.GetTempPath(), "fs-" + Guid.NewGuid().ToString("N")[..8]);
			Directory.CreateDirectory(dir, Private);
			return dir;
		}

		[TestMethod]
		public async Task SymlinkedOrOpenRuntimeDirectoryIsRefusedWithoutCreatingFiles()
		{
			var real = NewPrivateDir();
			var parent = Path.Combine(Path.GetTempPath(), "fl-" + Guid.NewGuid().ToString("N")[..8]);
			Directory.CreateDirectory(parent);
			var link = Path.Combine(parent, "run");
			Directory.CreateSymbolicLink(link, real);
			var open = Path.Combine(parent, "open");
			Directory.CreateDirectory(open, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
			try
			{
				foreach (var dir in new[] { link, open })
				{
					await using var service = new LinuxSingleInstanceService(new SingleInstanceOptions { DisableDBus = true, RuntimeDirectory = dir });
					// Cannot coordinate safely: runs as primary on its own, and creates nothing there
					Assert.IsTrue(await service.TryBecomePrimaryAsync(new InstanceRequest(InstanceRequestKind.CommandLine, "/", [])));
				}

				Assert.AreEqual(0, Directory.GetFileSystemEntries(real).Length);
				Assert.AreEqual(0, Directory.GetFileSystemEntries(open).Length);
			}
			finally
			{
				Directory.Delete(parent, true);
				Directory.Delete(real, true);
			}
		}

		[TestMethod]
		public async Task SymlinkedLockFileIsRefused()
		{
			var dir = NewPrivateDir();
			var victim = Path.Combine(dir, "victim");
			File.WriteAllText(victim, "keep");
			File.CreateSymbolicLink(Path.Combine(dir, "io.github.memergamer.LinuxFiles.lock"), victim);
			try
			{
				await using var service = new LinuxSingleInstanceService(new SingleInstanceOptions { DisableDBus = true, RuntimeDirectory = dir });
				Assert.IsTrue(await service.TryBecomePrimaryAsync(new InstanceRequest(InstanceRequestKind.CommandLine, "/", [])));
				Assert.IsFalse(File.Exists(Path.Combine(dir, "io.github.memergamer.LinuxFiles.sock")), "no listener was started");
				Assert.AreEqual("keep", File.ReadAllText(victim));
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}

		[TestMethod]
		public async Task RequestsFromAnotherUserAreDropped()
		{
			var dir = NewPrivateDir();
			try
			{
				var ours = ProcessIdentityNative.CurrentUserId;
				var options = new SingleInstanceOptions { DisableDBus = true, RuntimeDirectory = dir, PeerUserIdReader = _ => ours + 1 };
				await using var first = new LinuxSingleInstanceService(options);
				var received = new List<InstanceRequest>();
				first.RequestReceived += (_, r) => received.Add(r);
				Assert.IsTrue(await first.TryBecomePrimaryAsync(new InstanceRequest(InstanceRequestKind.CommandLine, "/", [])));

				await using var second = new LinuxSingleInstanceService(options with { PeerUserIdReader = null });
				// The primary sees a foreign peer uid, never acknowledges, and the second launch gives up forwarding
				Assert.IsTrue(await second.TryBecomePrimaryAsync(new InstanceRequest(InstanceRequestKind.CommandLine, "/w", ["/etc"])));
				Assert.AreEqual(0, received.Count);
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}

		[TestMethod]
		public async Task RequestsFromTheSameUserAreAcceptedWithRealPeerCredentials()
		{
			var dir = NewPrivateDir();
			try
			{
				var options = new SingleInstanceOptions { DisableDBus = true, RuntimeDirectory = dir };
				await using var first = new LinuxSingleInstanceService(options);
				var received = new System.Collections.Concurrent.ConcurrentQueue<InstanceRequest>();
				first.RequestReceived += (_, r) => received.Enqueue(r);
				Assert.IsTrue(await first.TryBecomePrimaryAsync(new InstanceRequest(InstanceRequestKind.CommandLine, "/", [])));

				await using var second = new LinuxSingleInstanceService(options);
				Assert.IsFalse(await second.TryBecomePrimaryAsync(new InstanceRequest(InstanceRequestKind.CommandLine, "/w", ["/etc"])));
				Assert.IsTrue(received.TryDequeue(out var request));
				Assert.AreEqual("/w", request.WorkingDirectory);
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}

		[TestMethod]
		public async Task OversizedAndMalformedMessagesAreIgnored()
		{
			var dir = NewPrivateDir();
			try
			{
				var options = new SingleInstanceOptions { DisableDBus = true, RuntimeDirectory = dir };
				await using var first = new LinuxSingleInstanceService(options);
				var received = new System.Collections.Concurrent.ConcurrentQueue<InstanceRequest>();
				first.RequestReceived += (_, r) => received.Enqueue(r);
				Assert.IsTrue(await first.TryBecomePrimaryAsync(new InstanceRequest(InstanceRequestKind.CommandLine, "/", [])));

				var socketPath = Path.Combine(dir, "io.github.memergamer.LinuxFiles.sock");

				// An argument bigger than the cap, then a NUL inside an argument
				foreach (var bad in new[] { new string('a', 20000), "ok\0evil" })
				{
					using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
					await socket.ConnectAsync(new System.Net.Sockets.UnixDomainSocketEndPoint(socketPath));
					using var stream = new System.Net.Sockets.NetworkStream(socket);
					using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
					{
						writer.Write(0);
						writer.Write("/w");
						writer.Write(1);
						writer.Write(bad);
					}

					var ack = new byte[1];
					try { Assert.AreEqual(0, await stream.ReadAsync(ack), "no acknowledgement for rejected messages"); }
					catch (IOException) { /* connection reset: also a rejection */ }
				}

				Assert.AreEqual(0, received.Count);
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}
	}
}
