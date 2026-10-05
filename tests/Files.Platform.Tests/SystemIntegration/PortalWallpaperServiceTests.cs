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

			public List<(string Content, string SetOn, bool ShowPreview)> Calls { get; } = new();

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
				string content;
				using (var fd = reader.ReadHandle<Microsoft.Win32.SafeHandles.SafeFileHandle>())
				{
					var data = new byte[64];
					var length = System.IO.RandomAccess.Read(fd, data, 0);
					content = Encoding.UTF8.GetString(data, 0, length);
				}

				var options = reader.ReadDictionaryOfStringToVariantValue();
				var token = options["handle_token"].GetString();
				lock (Calls)
					Calls.Add((content, options["set-on"].GetString(), options["show-preview"].GetBool()));

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

			// A name that URI parsing would mangle (%41 -> A) must reach the portal as the very same file
			var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fwall-" + Guid.NewGuid().ToString("N")[..8]);
			System.IO.Directory.CreateDirectory(dir);
			var first = System.IO.Path.Combine(dir, "%41 b.png");
			var second = System.IO.Path.Combine(dir, "c.png");
			System.IO.File.WriteAllText(first, "first");
			System.IO.File.WriteAllText(second, "second");

			Assert.IsTrue(await service.IsAvailableAsync());
			Assert.AreEqual(WallpaperResult.Applied, await service.SetAsync(first, WallpaperTarget.Background));
			Assert.AreEqual(WallpaperResult.Applied, await service.SetAsync(second, WallpaperTarget.LockScreen));
			System.IO.Directory.Delete(dir, true);

			var calls = portal.Calls.ToArray();
			Assert.AreEqual(2, calls.Length);
			Assert.AreEqual("first", calls[0].Content);
			Assert.AreEqual("background", calls[0].SetOn);
			Assert.IsTrue(calls[0].ShowPreview);
			Assert.AreEqual("lockscreen", calls[1].SetOn);
			Assert.AreEqual("second", calls[1].Content);
		}

		[TestMethod]
		public async Task ReportsCancelAndFailure()
		{
			using var bus = PrivateBus.Start();
			using var portal = await FakePortal.StartAsync(bus.Address);
			using var service = new PortalWallpaperService(bus.Address);

			var file = System.IO.Path.GetTempFileName();
			try
			{
				portal.ResponseCode = 1;
				Assert.AreEqual(WallpaperResult.Cancelled, await service.SetAsync(file, WallpaperTarget.Background));
				portal.ResponseCode = 2;
				Assert.AreEqual(WallpaperResult.Failed, await service.SetAsync(file, WallpaperTarget.Background));
			}
			finally
			{
				System.IO.File.Delete(file);
			}
		}

		[TestMethod]
		public async Task IsUnavailableWithoutAPortalAndRejectsRelativePaths()
		{
			using var bus = PrivateBus.Start();
			using var service = new PortalWallpaperService(bus.Address);

			Assert.IsFalse(await service.IsAvailableAsync());
			var file = System.IO.Path.GetTempFileName();
			try
			{
				Assert.AreEqual(WallpaperResult.Unavailable, await service.SetAsync(file, WallpaperTarget.Background));
			}
			finally
			{
				System.IO.File.Delete(file);
			}

			Assert.AreEqual(WallpaperResult.Failed, await service.SetAsync("relative.png", WallpaperTarget.Background));
		}
	}
}
