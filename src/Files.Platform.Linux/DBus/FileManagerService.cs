// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Instance;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Files.Platform.Linux.DBus
{
	/// <summary>
	/// Implements <c>org.freedesktop.FileManager1</c> so other applications' "Show in folder" opens Files.
	/// The name is only claimed while <see cref="StartAsync"/> is active and is never taken from another running file manager.
	/// </summary>
	public sealed class FileManagerService : IAsyncDisposable
	{
		/// <summary>The well-known bus name.</summary>
		public const string ServiceName = "org.freedesktop.FileManager1";

		/// <summary>The object path.</summary>
		public const string ObjectPath = "/org/freedesktop/FileManager1";

		private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

		private readonly string? busAddress;
		private readonly Action<InstanceRequest> sink;
		private DBusConnection? connection;

		/// <summary>
		/// Creates the service. Requests are delivered to <paramref name="sink"/>.
		/// </summary>
		public FileManagerService(string? busAddress, Action<InstanceRequest> sink)
		{
			this.busAddress = busAddress;
			this.sink = sink;
		}

		/// <summary>Gets whether this process currently owns the FileManager1 name.</summary>
		public bool IsRunning => connection is not null;

		/// <summary>
		/// Claims the name. Returns false if there is no session bus or another file manager already owns the name.
		/// </summary>
		public async Task<bool> StartAsync()
		{
			if (connection is not null)
				return true;

			var bus = await DBusSession.TryConnectAsync(busAddress, ConnectTimeout).ConfigureAwait(false);
			if (bus is null)
				return false;

			try
			{
				bus.AddMethodHandler(new Handler(sink));
				if (!await bus.TryRequestNameAsync(ServiceName, RequestNameOptions.None).ConfigureAwait(false))
				{
					bus.Dispose();
					return false;
				}

				connection = bus;
				return true;
			}
			catch (Exception ex) when (ex is DBusExceptionBase or InvalidOperationException)
			{
				bus.Dispose();
				return false;
			}
		}

		/// <summary>
		/// Releases the name.
		/// </summary>
		public Task StopAsync()
		{
			var bus = connection;
			connection = null;
			bus?.Dispose();
			return Task.CompletedTask;
		}

		/// <inheritdoc/>
		public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

		/// <summary>
		/// Converts <c>file://</c> URIs (or absolute paths) to local paths, dropping everything else.
		/// </summary>
		public static IReadOnlyList<string> UrisToPaths(IEnumerable<string> uris)
		{
			var paths = new List<string>();
			foreach (var value in uris)
			{
				if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
					paths.Add(uri.LocalPath);
				else if (value.StartsWith('/'))
					paths.Add(value);
			}

			return paths;
		}

		private static readonly byte[] IntrospectionXml = Encoding.UTF8.GetBytes("""
			<interface name="org.freedesktop.FileManager1">
			  <method name="ShowFolders"><arg type="as" name="URIs" direction="in"/><arg type="s" name="StartupId" direction="in"/></method>
			  <method name="ShowItems"><arg type="as" name="URIs" direction="in"/><arg type="s" name="StartupId" direction="in"/></method>
			  <method name="ShowItemProperties"><arg type="as" name="URIs" direction="in"/><arg type="s" name="StartupId" direction="in"/></method>
			</interface>
			""");

		private sealed class Handler : IPathMethodHandler
		{
			private readonly Action<InstanceRequest> sink;

			public Handler(Action<InstanceRequest> sink)
			{
				this.sink = sink;
			}

			public string Path => ObjectPath;

			public bool HandlesChildPaths => false;

			public ValueTask HandleMethodAsync(MethodContext context)
			{
				var request = context.Request;

				if (context.IsDBusIntrospectRequest)
				{
					context.ReplyIntrospectXml([IntrospectionXml]);
					return ValueTask.CompletedTask;
				}

				if (request.InterfaceAsString != ServiceName)
				{
					context.ReplyUnknownMethodError();
					return ValueTask.CompletedTask;
				}

				InstanceRequestKind kind;
				switch (request.MemberAsString)
				{
					case "ShowFolders": kind = InstanceRequestKind.ShowFolders; break;
					case "ShowItems": kind = InstanceRequestKind.ShowItems; break;
					case "ShowItemProperties": kind = InstanceRequestKind.ShowItemProperties; break;
					default:
						context.ReplyUnknownMethodError();
						return ValueTask.CompletedTask;
				}

				var reader = request.GetBodyReader();
				var uris = reader.ReadArrayOfString();
				var paths = UrisToPaths(uris);

				sink(new InstanceRequest(kind, Environment.CurrentDirectory, paths));

				if (!context.NoReplyExpected)
				{
					using var writer = context.CreateReplyWriter("");
					context.Reply(writer.CreateMessage());
				}

				return ValueTask.CompletedTask;
			}
		}
	}
}
