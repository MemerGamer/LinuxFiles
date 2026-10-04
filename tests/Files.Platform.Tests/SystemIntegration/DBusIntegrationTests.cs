// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Instance;
using Files.Platform.Linux.DBus;
using Files.Platform.Linux.Instance;
using Files.Platform.Linux.Notifications;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Files.Platform.Tests.SystemIntegration
{
	/// <summary>
	/// Everything here runs against a private dbus-daemon (see <see cref="PrivateBus"/>), never the user's session bus.
	/// </summary>
	[TestClass]
	public sealed class DBusIntegrationTests
	{
		private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

		private static async Task<InstanceRequest> NextAsync(BlockingQueue queue)
			=> await queue.TakeAsync(Wait);

		private sealed class BlockingQueue
		{
			private readonly ConcurrentQueue<InstanceRequest> items = new();
			private readonly System.Threading.SemaphoreSlim signal = new(0);

			public void Add(InstanceRequest request)
			{
				items.Enqueue(request);
				signal.Release();
			}

			public async Task<InstanceRequest> TakeAsync(TimeSpan timeout)
			{
				if (!await signal.WaitAsync(timeout))
					throw new TimeoutException("No request arrived.");

				items.TryDequeue(out var request);
				return request!;
			}
		}

		private delegate void WriteBody(ref MessageWriter writer);

		private static void WriteStrings(ref MessageWriter w, params string[] values)
		{
			var start = w.WriteArrayStart(DBusType.String);
			foreach (var v in values)
				w.WriteString(v);
			w.WriteArrayEnd(start);
		}

		private static MessageBuffer BuildCall(DBusConnection bus, string destination, string path, string iface, string member, string signature, WriteBody body)
		{
			var writer = bus.GetMessageWriter();
			try
			{
				writer.WriteMethodCallHeader(destination, path, iface, member, signature);
				body(ref writer);
				return writer.CreateMessage();
			}
			finally
			{
				writer.Dispose();
			}
		}

		[TestMethod]
		public async Task SecondLaunchOnBusForwardsToFirstAndExits()
		{
			using var bus = PrivateBus.Start();
			var options = new SingleInstanceOptions { BusAddress = bus.Address };
			await using var first = new LinuxSingleInstanceService(options);
			await using var second = new LinuxSingleInstanceService(options);

			var received = new BlockingQueue();
			first.RequestReceived += (_, r) => received.Add(r);

			Assert.IsTrue(await first.TryBecomePrimaryAsync(new InstanceRequest(InstanceRequestKind.CommandLine, "/home/u", [])));

			var forwarded = new InstanceRequest(InstanceRequestKind.CommandLine, "/work", ["--select", "/etc/hosts", "-t", "/usr"]);
			Assert.IsFalse(await second.TryBecomePrimaryAsync(forwarded));

			var request = await NextAsync(received);
			Assert.AreEqual("/work", request.WorkingDirectory);
			CollectionAssert.AreEqual(new[] { "--select", "/etc/hosts", "-t", "/usr" }, request.Arguments.ToArray());
		}

		[TestMethod]
		public async Task FreedesktopApplicationOpenAndActivateReachTheHandler()
		{
			using var bus = PrivateBus.Start();
			var options = new SingleInstanceOptions { BusAddress = bus.Address };
			await using var primary = new LinuxSingleInstanceService(options);
			var received = new BlockingQueue();
			primary.RequestReceived += (_, r) => received.Add(r);
			Assert.IsTrue(await primary.TryBecomePrimaryAsync(new InstanceRequest(InstanceRequestKind.CommandLine, "/", [])));

			using var client = await DBusSession.TryConnectAsync(bus.Address, Wait) ?? throw new AssertFailedException("no bus");
			const string Path = "/io/github/memergamer/LinuxFiles";

			await client.CallMethodAsync(BuildCall(client, options.BusName, Path, "org.freedesktop.Application", "Open", "asa{sv}", (ref MessageWriter w) =>
			{
				WriteStrings(ref w, "file:///tmp/a%20b", "https://example.org/skip");
				w.WriteDictionary(new System.Collections.Generic.Dictionary<string, VariantValue>());
			}));
			var open = await NextAsync(received);
			CollectionAssert.AreEqual(new[] { "/tmp/a b" }, open.Arguments.ToArray());

			await client.CallMethodAsync(BuildCall(client, options.BusName, Path, "org.freedesktop.Application", "Activate", "a{sv}",
				(ref MessageWriter w) => w.WriteDictionary(new System.Collections.Generic.Dictionary<string, VariantValue>())));
			Assert.AreEqual(0, (await NextAsync(received)).Arguments.Count);
		}

		[TestMethod]
		public async Task RequestsBeforeSubscriptionAreQueued()
		{
			using var bus = PrivateBus.Start();
			var options = new SingleInstanceOptions { BusAddress = bus.Address };
			await using var first = new LinuxSingleInstanceService(options);
			await using var second = new LinuxSingleInstanceService(options);
			Assert.IsTrue(await first.TryBecomePrimaryAsync(new InstanceRequest(InstanceRequestKind.CommandLine, "/", [])));
			Assert.IsFalse(await second.TryBecomePrimaryAsync(new InstanceRequest(InstanceRequestKind.CommandLine, "/early", ["/x"])));

			var received = new BlockingQueue();
			first.RequestReceived += (_, r) => received.Add(r);
			Assert.AreEqual("/early", (await NextAsync(received)).WorkingDirectory);
		}

		[TestMethod]
		public async Task SocketFallbackForwardsWithoutABus()
		{
			var runtime = Path.Combine(Path.GetTempPath(), "fr-" + Guid.NewGuid().ToString("N")[..8]);
			Directory.CreateDirectory(runtime);
			try
			{
				var options = new SingleInstanceOptions { DisableDBus = true, RuntimeDirectory = runtime };
				await using var first = new LinuxSingleInstanceService(options);
				var received = new BlockingQueue();
				first.RequestReceived += (_, r) => received.Add(r);
				Assert.IsTrue(await first.TryBecomePrimaryAsync(new InstanceRequest(InstanceRequestKind.CommandLine, "/", [])));

				await using var second = new LinuxSingleInstanceService(options);
				Assert.IsFalse(await second.TryBecomePrimaryAsync(new InstanceRequest(InstanceRequestKind.ShowItems, "/w", ["/etc/hosts", "ünï"])));

				var request = await NextAsync(received);
				Assert.AreEqual(InstanceRequestKind.ShowItems, request.Kind);
				Assert.AreEqual("/w", request.WorkingDirectory);
				CollectionAssert.AreEqual(new[] { "/etc/hosts", "ünï" }, request.Arguments.ToArray());

				// After the first instance exits the next launch becomes primary again
				await first.DisposeAsync();
				await using var third = new LinuxSingleInstanceService(options);
				Assert.IsTrue(await third.TryBecomePrimaryAsync(new InstanceRequest(InstanceRequestKind.CommandLine, "/", [])));
			}
			finally
			{
				Directory.Delete(runtime, true);
			}
		}

		[TestMethod]
		public async Task FileManagerServiceDeliversShowItemsAndDoesNotStealTheName()
		{
			using var bus = PrivateBus.Start();
			var received = new BlockingQueue();
			await using var service = new FileManagerService(bus.Address, received.Add);
			Assert.IsTrue(await service.StartAsync());

			using var client = await DBusSession.TryConnectAsync(bus.Address, Wait) ?? throw new AssertFailedException("no bus");
			await client.CallMethodAsync(BuildCall(client, FileManagerService.ServiceName, FileManagerService.ObjectPath, FileManagerService.ServiceName, "ShowItems", "ass", (ref MessageWriter w) =>
			{
				WriteStrings(ref w, "file:///home/u/My%20File.txt");
				w.WriteString("startup-id");
			}));

			var request = await NextAsync(received);
			Assert.AreEqual(InstanceRequestKind.ShowItems, request.Kind);
			CollectionAssert.AreEqual(new[] { "/home/u/My File.txt" }, request.Arguments.ToArray());

			await using var rival = new FileManagerService(bus.Address, _ => { });
			Assert.IsFalse(await rival.StartAsync());

			await service.StopAsync();
			Assert.IsTrue(await rival.StartAsync());
		}

		[TestMethod]
		public void FileUrisAreConvertedToPaths()
		{
			var paths = FileManagerService.UrisToPaths(["file:///a/b%20c", "/already/path", "http://x/y", "relative"]);
			CollectionAssert.AreEqual(new[] { "/a/b c", "/already/path" }, paths.ToArray());
		}

		private sealed class FakeNotificationServer : IPathMethodHandler
		{
			public BlockingQueue Calls { get; } = new();
			public System.Collections.Concurrent.ConcurrentQueue<(string Summary, string Body, string App)> Seen { get; } = new();
			public System.Threading.SemaphoreSlim Signal { get; } = new(0);

			public string Path => "/org/freedesktop/Notifications";

			public bool HandlesChildPaths => false;

			public ValueTask HandleMethodAsync(MethodContext context)
			{
				if (context.Request.MemberAsString == "Notify")
				{
					var reader = context.Request.GetBodyReader();
					var app = reader.ReadString();
					reader.ReadUInt32();
					reader.ReadString();
					var summary = reader.ReadString();
					var body = reader.ReadString();
					Seen.Enqueue((summary, body, app));
					Signal.Release();

					using var writer = context.CreateReplyWriter("u");
					writer.WriteUInt32(42);
					context.Reply(writer.CreateMessage());
				}
				else
				{
					context.ReplyUnknownMethodError();
				}

				return ValueTask.CompletedTask;
			}
		}

		[TestMethod]
		public async Task NotificationsGoThroughTheNotificationsInterface()
		{
			using var bus = PrivateBus.Start();
			using var daemon = await DBusSession.TryConnectAsync(bus.Address, Wait) ?? throw new AssertFailedException("no bus");
			var server = new FakeNotificationServer();
			daemon.AddMethodHandler(server);
			Assert.IsTrue(await daemon.TryRequestNameAsync("org.freedesktop.Notifications", RequestNameOptions.None));

			using var service = new DBusNotificationService(bus.Address);
			Assert.IsTrue(await service.NotifyAsync("Title ünï", "Body text"));
			Assert.IsTrue(await server.Signal.WaitAsync(Wait));
			Assert.IsTrue(server.Seen.TryDequeue(out var seen));
			Assert.AreEqual(("Title ünï", "Body text", "Files"), seen);
		}

		[TestMethod]
		public async Task NotificationsReturnFalseWithoutADaemon()
		{
			using var bus = PrivateBus.Start();
			using var service = new DBusNotificationService(bus.Address);
			Assert.IsFalse(await service.NotifyAsync("t", "b"));

			using var noBus = new DBusNotificationService("unix:path=/nonexistent/bus");
			Assert.IsFalse(await noBus.NotifyAsync("t", "b"));
		}
	}
}
