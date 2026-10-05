// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.FileChooser;
using Files.Platform.Linux.DBus;
using Files.Platform.Linux.FileChooser;
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
	/// Talks to a fake FileChooser portal on a private bus. The real session bus and real dialogs are never touched.
	/// </summary>
	[TestClass]
	public sealed class PortalFileChooserServiceTests
	{
		private sealed record Call(string Method, string Parent, string Title, Dictionary<string, VariantValue> Options);

		private sealed class FakePortal : IPathMethodHandler, IDisposable
		{
			private readonly DBusConnection connection;

			public List<Call> Calls { get; } = new();

			public uint ResponseCode { get; set; }

			public string[] Uris { get; set; } = [];

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

				var reader = request.GetBodyReader();
				var parent = reader.ReadString();
				var title = reader.ReadString();
				var options = reader.ReadDictionaryOfStringToVariantValue();
				var token = options["handle_token"].GetString();
				lock (Calls)
					Calls.Add(new Call(request.MemberAsString ?? "", parent, title, options));

				var handle = "/org/freedesktop/portal/desktop/request/fake/" + token;
				using (var reply = context.CreateReplyWriter("o"))
				{
					reply.WriteObjectPath(handle);
					context.Reply(reply.CreateMessage());
				}

				var code = ResponseCode;
				var uris = Uris;
				_ = Task.Run(async () =>
				{
					await Task.Delay(100);
					using var writer = connection.GetMessageWriter();
					writer.WriteSignalHeader(null, handle, "org.freedesktop.portal.Request", "Response", "ua{sv}");
					writer.WriteUInt32(code);
					var start = writer.WriteDictionaryStart();
					if (code == 0)
					{
						writer.WriteDictionaryEntryStart();
						writer.WriteString("uris");
						writer.WriteSignature("as");
						var array = writer.WriteArrayStart(DBusType.String);
						foreach (var uri in uris)
							writer.WriteString(uri);
						writer.WriteArrayEnd(array);
					}

					writer.WriteDictionaryEnd(start);
					connection.TrySendMessage(writer.CreateMessage());
				});
				return ValueTask.CompletedTask;
			}

			public void Dispose() => connection.Dispose();
		}

		[TestMethod]
		public async Task OpensASingleFileWithFiltersAndParentWindow()
		{
			using var bus = PrivateBus.Start();
			using var portal = await FakePortal.StartAsync(bus.Address);
			using var service = new PortalFileChooserService(bus.Address);
			portal.Uris = ["file:///tmp/a%20b%25.png"];

			var result = await service.ChooseAsync(new FileChooserRequest
			{
				Title = "Pick",
				ParentWindowId = 0x1a2b,
				Filters = [new FileChooserFilter("Images", ["*.png", "*.jpg"]), new FileChooserFilter("All", ["*"])],
			});

			Assert.AreEqual(FileChooserStatus.Selected, result.Status);
			CollectionAssert.AreEqual(new[] { "/tmp/a b%.png" }, result.Paths.ToArray());
			var call = portal.Calls.Single();
			Assert.AreEqual("OpenFile", call.Method);
			Assert.AreEqual("x11:1a2b", call.Parent);
			Assert.AreEqual("Pick", call.Title);
			Assert.IsFalse(call.Options["multiple"].GetBool());
			Assert.IsFalse(call.Options.ContainsKey("directory"));

			var filters = call.Options["filters"];
			Assert.AreEqual(2, filters.Count);
			Assert.AreEqual("Images", filters.GetItem(0).GetItem(0).GetString());
			var globs = filters.GetItem(0).GetItem(1);
			Assert.AreEqual(2, globs.Count);
			Assert.AreEqual(0u, globs.GetItem(0).GetItem(0).GetUInt32());
			Assert.AreEqual("*.jpg", globs.GetItem(1).GetItem(1).GetString());
		}

		[TestMethod]
		public async Task OpensSeveralFilesAndAnEmptyParentWhenUnknown()
		{
			using var bus = PrivateBus.Start();
			using var portal = await FakePortal.StartAsync(bus.Address);
			using var service = new PortalFileChooserService(bus.Address);
			portal.Uris = ["file:///tmp/one", "file://localhost/tmp/two", "https://example.com/x", "file://otherhost/tmp/three", "file:///tmp/%00bad"];

			var result = await service.ChooseAsync(new FileChooserRequest { Multiple = true });

			Assert.AreEqual(FileChooserStatus.Selected, result.Status);
			CollectionAssert.AreEqual(new[] { "/tmp/one", "/tmp/two" }, result.Paths.ToArray());
			var call = portal.Calls.Single();
			Assert.AreEqual("", call.Parent);
			Assert.IsTrue(call.Options["multiple"].GetBool());
			Assert.IsFalse(call.Options.ContainsKey("filters"));
		}

		[TestMethod]
		public async Task PicksADirectory()
		{
			using var bus = PrivateBus.Start();
			using var portal = await FakePortal.StartAsync(bus.Address);
			using var service = new PortalFileChooserService(bus.Address);
			portal.Uris = ["file:///home/user/Documents"];

			var result = await service.ChooseAsync(new FileChooserRequest
			{
				PickFolder = true,
				Multiple = true,
				Filters = [new FileChooserFilter("Ignored", ["*"])],
			});

			CollectionAssert.AreEqual(new[] { "/home/user/Documents" }, result.Paths.ToArray());
			var call = portal.Calls.Single();
			Assert.AreEqual("OpenFile", call.Method);
			Assert.IsTrue(call.Options["directory"].GetBool());
			Assert.IsFalse(call.Options["multiple"].GetBool());
			Assert.IsFalse(call.Options.ContainsKey("filters"));
		}

		[TestMethod]
		public async Task SavesWithNameFolderAndFilters()
		{
			using var bus = PrivateBus.Start();
			using var portal = await FakePortal.StartAsync(bus.Address);
			using var service = new PortalFileChooserService(bus.Address);
			portal.Uris = ["file:///tmp/export.zip"];

			var result = await service.ChooseAsync(new FileChooserRequest
			{
				Save = true,
				CurrentName = "export.zip",
				CurrentFolder = "/tmp",
				Filters = [new FileChooserFilter("Zip", ["*.zip"])],
			});

			CollectionAssert.AreEqual(new[] { "/tmp/export.zip" }, result.Paths.ToArray());
			var call = portal.Calls.Single();
			Assert.AreEqual("SaveFile", call.Method);
			Assert.AreEqual("export.zip", call.Options["current_name"].GetString());
			CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("/tmp\0"), call.Options["current_folder"].GetArray<byte>());
			Assert.AreEqual(1, call.Options["filters"].Count);
			Assert.IsFalse(call.Options.ContainsKey("multiple"));
		}

		[TestMethod]
		public async Task ReportsCancelAndUnusableSelections()
		{
			using var bus = PrivateBus.Start();
			using var portal = await FakePortal.StartAsync(bus.Address);
			using var service = new PortalFileChooserService(bus.Address);

			portal.ResponseCode = 1;
			var cancelled = await service.ChooseAsync(new FileChooserRequest());
			Assert.AreEqual(FileChooserStatus.Cancelled, cancelled.Status);
			Assert.AreEqual(0, cancelled.Paths.Count);

			portal.ResponseCode = 0;
			portal.Uris = ["https://example.com/x"];
			Assert.AreEqual(FileChooserStatus.Cancelled, (await service.ChooseAsync(new FileChooserRequest())).Status);
		}

		[TestMethod]
		public async Task IsUnavailableWithoutAPortal()
		{
			using var bus = PrivateBus.Start();
			using var service = new PortalFileChooserService(bus.Address);

			Assert.AreEqual(FileChooserStatus.Unavailable, (await service.ChooseAsync(new FileChooserRequest())).Status);
		}

		[TestMethod]
		public async Task IsUnavailableWithoutABus()
		{
			using var service = new PortalFileChooserService("unix:path=/nonexistent/files-test-bus");

			Assert.AreEqual(FileChooserStatus.Unavailable, (await service.ChooseAsync(new FileChooserRequest())).Status);
		}
	}
}
