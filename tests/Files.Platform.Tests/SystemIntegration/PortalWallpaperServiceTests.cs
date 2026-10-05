// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Wallpaper;
using Files.Platform.Linux.DBus;
using Files.Platform.Linux.Wallpaper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Files.Platform.Tests.SystemIntegration
{
	/// <summary>
	/// Talks to a fake portal on a private bus. The real session bus and the real wallpaper are never touched.
	/// </summary>
	[TestClass]
	public sealed class PortalWallpaperServiceTests
	{
		private sealed class FakePortal : IPathMethodHandler, IDisposable
		{
			private readonly DBusConnection connection;

			public List<(string Uri, string SetOn, bool ShowPreview)> Calls { get; } = new();

			public uint ResponseCode { get; set; }

			public string Path => "/org/freedesktop/portal/desktop";

			public bool HandlesChildPaths => false;

			private FakePortal(DBusConnection connection) => this.connection = connection;

			public static async Task<FakePortal> StartAsync(string address)
			{
				var bus = await DBusSession.TryConnectAsync(address, TimeSpan.FromSeconds(5)) ?? throw new InvalidOperationException("no bus");
				var fake = new FakePortal(bus);
				bus.AddMethodHandler(fake);
				await bus.TryRequestNameAsync("org.freedesktop.portal.Desktop", RequestNameOptions.None);
				return fake;
			}

			public ValueTask HandleMethodAsync(MethodContext context)
			{
				var request = context.Request;
				if (context.IsDBusIntrospectRequest)
				{
					context.ReplyIntrospectXml([Encoding.UTF8.GetBytes("<node/>")]);
					return ValueTask.CompletedTask;
				}

				if (request.MemberAsString == "Get")
				{
					using var version = context.CreateReplyWriter("v");
					version.WriteVariantUInt32(1);
					context.Reply(version.CreateMessage());
					return ValueTask.CompletedTask;
				}

				var reader = request.GetBodyReader();
				reader.ReadString();
				var uri = reader.ReadString();
				var options = reader.ReadDictionaryOfStringToVariantValue();
				var token = options["handle_token"].GetString();
				lock (Calls)
					Calls.Add((uri, options["set-on"].GetString(), options["show-preview"].GetBool()));

				var handle = "/org/freedesktop/portal/desktop/request/fake/" + token;
				using (var reply = context.CreateReplyWriter("o"))
				{
					reply.WriteObjectPath(handle);
					context.Reply(reply.CreateMessage());
				}

				var code = ResponseCode;
				_ = Task.Run(async () =>
				{
					await Task.Delay(100);
					using var writer = connection.GetMessageWriter();
					writer.WriteSignalHeader(null, handle, "org.freedesktop.portal.Request", "Response", "ua{sv}");
					writer.WriteUInt32(code);
					var start = writer.WriteDictionaryStart();
					writer.WriteDictionaryEnd(start);
					connection.TrySendMessage(writer.CreateMessage());
				});
				return ValueTask.CompletedTask;
			}

			public void Dispose() => connection.Dispose();
		}

		[TestMethod]
		public async Task SetsBackgroundAndLockScreenThroughThePortal()
		{
			using var bus = PrivateBus.Start();
			using var portal = await FakePortal.StartAsync(bus.Address);
			using var service = new PortalWallpaperService(bus.Address);

			Assert.IsTrue(await service.IsAvailableAsync());
			Assert.AreEqual(WallpaperResult.Applied, await service.SetAsync("/tmp/a b.png", WallpaperTarget.Background));
			Assert.AreEqual(WallpaperResult.Applied, await service.SetAsync("/tmp/c.png", WallpaperTarget.LockScreen));

			var calls = portal.Calls.ToArray();
			Assert.AreEqual(2, calls.Length);
			Assert.AreEqual("file:///tmp/a%20b.png", calls[0].Uri);
			Assert.AreEqual("background", calls[0].SetOn);
			Assert.IsTrue(calls[0].ShowPreview);
			Assert.AreEqual("lockscreen", calls[1].SetOn);
		}

		[TestMethod]
		public async Task ReportsCancelAndFailure()
		{
			using var bus = PrivateBus.Start();
			using var portal = await FakePortal.StartAsync(bus.Address);
			using var service = new PortalWallpaperService(bus.Address);

			portal.ResponseCode = 1;
			Assert.AreEqual(WallpaperResult.Cancelled, await service.SetAsync("/tmp/a.png", WallpaperTarget.Background));
			portal.ResponseCode = 2;
			Assert.AreEqual(WallpaperResult.Failed, await service.SetAsync("/tmp/a.png", WallpaperTarget.Background));
		}

		[TestMethod]
		public async Task IsUnavailableWithoutAPortalAndRejectsRelativePaths()
		{
			using var bus = PrivateBus.Start();
			using var service = new PortalWallpaperService(bus.Address);

			Assert.IsFalse(await service.IsAvailableAsync());
			Assert.AreEqual(WallpaperResult.Unavailable, await service.SetAsync("/tmp/a.png", WallpaperTarget.Background));
			Assert.AreEqual(WallpaperResult.Failed, await service.SetAsync("relative.png", WallpaperTarget.Background));
		}
	}
}
