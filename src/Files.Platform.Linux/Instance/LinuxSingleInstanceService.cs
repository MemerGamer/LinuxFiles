// Copyright (c) Files Community
// Licensed under the MIT License.

#pragma warning disable CA1416

using Files.Platform.Abstractions.Instance;
using Files.Platform.Linux.DBus;
using Files.Platform.Linux.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Files.Platform.Linux.Instance
{
	/// <summary>
	/// Configures <see cref="LinuxSingleInstanceService"/>.
	/// </summary>
	public sealed record SingleInstanceOptions
	{
		/// <summary>Gets the bus address. Null means <c>$DBUS_SESSION_BUS_ADDRESS</c>. Tests pass a private bus.</summary>
		public string? BusAddress { get; init; }

		/// <summary>Gets the well-known name claimed on the bus.</summary>
		public string BusName { get; init; } = "io.github.memergamer.LinuxFiles";

		/// <summary>Gets a value indicating whether to skip D-Bus and use only the socket (forced fallback).</summary>
		public bool DisableDBus { get; init; }

		/// <summary>Gets the directory for the fallback socket and lock file. Null means <c>$XDG_RUNTIME_DIR</c>.</summary>
		public string? RuntimeDirectory { get; init; }

		/// <summary>Gets the file ownership lookup (a test seam; defaults to <c>statx</c>).</summary>
		public IFileOwnershipInspector? Inspector { get; init; }

		/// <summary>Gets the user id this process runs as (a test seam; defaults to <c>geteuid</c>).</summary>
		public uint? CurrentUserId { get; init; }

		/// <summary>Gets how the user id of a connecting peer is read (a test seam; defaults to <c>SO_PEERCRED</c>).</summary>
		public Func<Socket, uint?>? PeerUserIdReader { get; init; }
	}

	/// <summary>
	/// Decides whether directories and entries used for the socket fallback can be trusted.
	/// </summary>
	public static class SingleInstanceSecurity
	{
		/// <summary>
		/// A directory is trusted when it is a real directory (not a symbolic link) owned by <paramref name="userId"/> that nobody else can read, write or enter (<c>0700</c>).
		/// </summary>
		public static bool IsTrustedDirectory(string path, IFileOwnershipInspector inspector, uint userId)
		{
			if (!inspector.TryGetInfo(path, out var info))
				return false;

			const UnixFileMode Others = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
				| UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

			return info.IsDirectory && !info.IsSymbolicLink && info.OwnerUserId == userId && (info.Mode & Others) == 0;
		}

		/// <summary>
		/// An entry (lock file or socket) is safe to reuse when it does not exist, or is a non-directory, non-symlink owned by <paramref name="userId"/>.
		/// </summary>
		public static bool IsSafeEntry(string path, IFileOwnershipInspector inspector, uint userId)
		{
			if (!inspector.TryGetInfo(path, out var info))
				return !File.Exists(path) && !Directory.Exists(path);

			return !info.IsDirectory && !info.IsSymbolicLink && info.OwnerUserId == userId;
		}
	}

	/// <summary>
	/// Single instance through a D-Bus well-known name (<c>org.freedesktop.Application</c> plus a <c>Launch</c> method for full command lines);
	/// without a session bus, through a Unix domain socket in <c>$XDG_RUNTIME_DIR</c> guarded by a lock file (<c>flock</c>).
	/// </summary>
	public sealed class LinuxSingleInstanceService : ISingleInstanceService
	{
		private const string ApplicationInterface = "org.freedesktop.Application";
		private const string InstanceInterface = "io.github.memergamer.LinuxFiles.Instance";
		private static readonly TimeSpan BusTimeout = TimeSpan.FromSeconds(3);
		private static readonly TimeSpan ForwardTimeout = TimeSpan.FromSeconds(5);

		private readonly SingleInstanceOptions options;
		private readonly object gate = new();
		private readonly Queue<InstanceRequest> pending = new();
		private EventHandler<InstanceRequest>? requestReceived;

		private DBusConnection? connection;
		private FileStream? lockFile;
		private Socket? listener;
		private string? socketPath;
		private CancellationTokenSource? acceptCts;

		/// <summary>
		/// Creates the service.
		/// </summary>
		public LinuxSingleInstanceService(SingleInstanceOptions? options = null)
		{
			this.options = options ?? new SingleInstanceOptions();
		}

		/// <inheritdoc/>
		public event EventHandler<InstanceRequest>? RequestReceived
		{
			add
			{
				List<InstanceRequest> toDeliver;
				lock (gate)
				{
					requestReceived += value;
					toDeliver = [.. pending];
					pending.Clear();
				}

				foreach (var request in toDeliver)
					value?.Invoke(this, request);
			}
			remove
			{
				lock (gate)
					requestReceived -= value;
			}
		}

		/// <summary>
		/// Raises a request as if it came from another instance (used by other services such as FileManager1).
		/// </summary>
		public void Post(InstanceRequest request)
		{
			EventHandler<InstanceRequest>? handler;
			lock (gate)
			{
				handler = requestReceived;
				if (handler is null)
				{
					pending.Enqueue(request);
					return;
				}
			}

			handler(this, request);
		}

		/// <inheritdoc/>
		public async Task<bool> TryBecomePrimaryAsync(InstanceRequest launchRequest, CancellationToken cancellationToken = default)
		{
			if (!options.DisableDBus)
			{
				var bus = await DBusSession.TryConnectAsync(options.BusAddress, BusTimeout).ConfigureAwait(false);
				if (bus is not null)
					return await TryBecomePrimaryOnBusAsync(bus, launchRequest, cancellationToken).ConfigureAwait(false);
			}

			return await TryBecomePrimaryOnSocketAsync(launchRequest, cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public ValueTask DisposeAsync()
		{
			acceptCts?.Cancel();
			listener?.Dispose();
			listener = null;

			if (socketPath is not null)
			{
				try { File.Delete(socketPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
			}

			lockFile?.Dispose();
			lockFile = null;

			connection?.Dispose();
			connection = null;
			return ValueTask.CompletedTask;
		}

		// ---- D-Bus ----

		private async Task<bool> TryBecomePrimaryOnBusAsync(DBusConnection bus, InstanceRequest launchRequest, CancellationToken cancellationToken)
		{
			try
			{
				var path = ObjectPathFor(options.BusName);
				bus.AddMethodHandler(new ApplicationHandler(path, this));

				if (await bus.TryRequestNameAsync(options.BusName, RequestNameOptions.None).ConfigureAwait(false))
				{
					connection = bus;
					return true;
				}

				// Somebody else owns the name: hand over the command line
				var message = BuildLaunch(bus, path, launchRequest);

				await bus.CallTracedAsync("dbus.instance", message).WaitAsync(ForwardTimeout, cancellationToken).ConfigureAwait(false);
				bus.Dispose();
				return false;
			}
			catch (Exception ex) when (ex is TimeoutException or DBusExceptionBase or InvalidOperationException)
			{
				// The owner did not answer: run on our own rather than refusing to start
				connection = bus;
				return true;
			}
		}

		private MessageBuffer BuildLaunch(DBusConnection bus, string path, InstanceRequest launchRequest)
		{
			using var writer = bus.GetMessageWriter();
			writer.WriteMethodCallHeader(options.BusName, path, InstanceInterface, "Launch", "sias");
			writer.WriteString(launchRequest.WorkingDirectory);
			writer.WriteInt32((int)launchRequest.Kind);
			var start = writer.WriteArrayStart(DBusType.String);
			foreach (var argument in launchRequest.Arguments)
				writer.WriteString(argument);
			writer.WriteArrayEnd(start);

			return writer.CreateMessage();
		}

		private static string ObjectPathFor(string busName) => "/" + busName.Replace('.', '/');

		private sealed class ApplicationHandler : IPathMethodHandler
		{
			private readonly LinuxSingleInstanceService owner;

			public ApplicationHandler(string path, LinuxSingleInstanceService owner)
			{
				Path = path;
				this.owner = owner;
			}

			public string Path { get; }

			public bool HandlesChildPaths => false;

			public ValueTask HandleMethodAsync(MethodContext context)
			{
				var request = context.Request;

				if (context.IsDBusIntrospectRequest)
				{
					context.ReplyIntrospectXml([Introspection]);
					return ValueTask.CompletedTask;
				}

				var iface = request.InterfaceAsString;
				var member = request.MemberAsString;
				var cwd = Environment.CurrentDirectory;

				if (iface == ApplicationInterface && member == "Activate")
				{
					owner.Post(new InstanceRequest(InstanceRequestKind.CommandLine, cwd, []));
				}
				else if (iface == ApplicationInterface && member == "Open")
				{
					var paths = FileManagerService.UrisToPaths(request.GetBodyReader().ReadArrayOfString());
					owner.Post(new InstanceRequest(InstanceRequestKind.CommandLine, cwd, paths));
				}
				else if (iface == ApplicationInterface && member == "ActivateAction")
				{
					var action = request.GetBodyReader().ReadString();
					string[] args = action switch
					{
						"new-tab" => ["--new-tab"],
						"new-window" => ["--new-window"],
						_ => [],
					};
					owner.Post(new InstanceRequest(InstanceRequestKind.CommandLine, cwd, args));
				}
				else if (iface == InstanceInterface && member == "Launch")
				{
					var reader = request.GetBodyReader();
					var directory = reader.ReadString();
					var kind = reader.ReadInt32();
					var args = reader.ReadArrayOfString();
					owner.Post(new InstanceRequest(Enum.IsDefined((InstanceRequestKind)kind) ? (InstanceRequestKind)kind : InstanceRequestKind.CommandLine, directory, args));
				}
				else
				{
					context.ReplyUnknownMethodError();
					return ValueTask.CompletedTask;
				}

				if (!context.NoReplyExpected)
				{
					using var writer = context.CreateReplyWriter("");
					context.Reply(writer.CreateMessage());
				}

				return ValueTask.CompletedTask;
			}

			private static readonly byte[] Introspection = Encoding.UTF8.GetBytes("""
				<interface name="org.freedesktop.Application">
				  <method name="Activate"><arg type="a{sv}" name="platform_data" direction="in"/></method>
				  <method name="Open"><arg type="as" name="uris" direction="in"/><arg type="a{sv}" name="platform_data" direction="in"/></method>
				  <method name="ActivateAction"><arg type="s" name="action_name" direction="in"/><arg type="av" name="parameter" direction="in"/><arg type="a{sv}" name="platform_data" direction="in"/></method>
				</interface>
				<interface name="io.github.memergamer.LinuxFiles.Instance">
				  <method name="Launch"><arg type="s" name="cwd" direction="in"/><arg type="i" name="kind" direction="in"/><arg type="as" name="arguments" direction="in"/></method>
				</interface>
				""");
		}

		// ---- Unix socket fallback ----

		private IFileOwnershipInspector Inspector => options.Inspector ?? new StatxFileOwnershipInspector();

		private uint UserId => options.CurrentUserId ?? ProcessIdentityNative.CurrentUserId;

		/// <summary>
		/// Returns a directory only this user controls, or null if none can be trusted (then there is no socket coordination).
		/// </summary>
		private string? ResolveRuntimeDirectory()
		{
			var dir = options.RuntimeDirectory ?? Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
			if (!string.IsNullOrEmpty(dir) && Path.IsPathRooted(dir))
				return SingleInstanceSecurity.IsTrustedDirectory(dir, Inspector, UserId) ? dir : null;

			// No runtime directory: a private, randomly named directory (created 0700). Other launches cannot find it, which only
			// costs single-instance coordination, never safety. Never a predictable path under /tmp.
			try
			{
				var created = Directory.CreateTempSubdirectory("files-").FullName;
				return SingleInstanceSecurity.IsTrustedDirectory(created, Inspector, UserId) ? created : null;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return null;
			}
		}

		private async Task<bool> TryBecomePrimaryOnSocketAsync(InstanceRequest launchRequest, CancellationToken cancellationToken)
		{
			var dir = ResolveRuntimeDirectory();
			if (dir is null)
				return true;

			var lockPath = Path.Combine(dir, options.BusName + ".lock");
			var path = Path.Combine(dir, options.BusName + ".sock");

			// A Unix socket path is limited to ~108 bytes; if ours does not fit we cannot coordinate
			if (Encoding.UTF8.GetByteCount(path) > 100)
				return true;

			if (!SingleInstanceSecurity.IsSafeEntry(lockPath, Inspector, UserId) || !SingleInstanceSecurity.IsSafeEntry(path, Inspector, UserId))
				return true;

			// .NET takes an advisory flock() on the file for FileShare.None, released when the process exits (even if it crashes)
			try
			{
				lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			}
			catch (IOException)
			{
				lockFile = null;
			}

			if (lockFile is not null)
			{
				try
				{
					StartListening(path);
					return true;
				}
				catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException)
				{
					lockFile.Dispose();
					lockFile = null;
					return true;
				}
			}

			// Another process holds the lock: it is (or is about to be) listening
			var deadline = DateTime.UtcNow + ForwardTimeout;
			while (DateTime.UtcNow < deadline)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (await TrySendAsync(path, launchRequest, cancellationToken).ConfigureAwait(false))
					return false;

				await Task.Delay(100, cancellationToken).ConfigureAwait(false);
			}

			return true;
		}

		private void StartListening(string path)
		{
			socketPath = path;
			try { File.Delete(path); } catch (IOException) { }

			var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			socket.Bind(new UnixDomainSocketEndPoint(path));
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
			socket.Listen(8);
			listener = socket;

			acceptCts = new CancellationTokenSource();
			_ = Task.Run(() => AcceptLoopAsync(socket, acceptCts.Token));
		}

		private async Task AcceptLoopAsync(Socket socket, CancellationToken token)
		{
			while (!token.IsCancellationRequested)
			{
				Socket client;
				try
				{
					client = await socket.AcceptAsync(token).ConfigureAwait(false);
				}
				catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
				{
					return;
				}

				_ = Task.Run(() => HandleClientAsync(client, token), token);
			}
		}

		private async Task HandleClientAsync(Socket client, CancellationToken token)
		{
			try
			{
				using var stream = new NetworkStream(client, ownsSocket: true);
				using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
				timeout.CancelAfter(ForwardTimeout);

				// Only the same user may talk to us (the directory is 0700 already; this is the second line of defence)
				var peerUid = (options.PeerUserIdReader ?? ProcessIdentityNative.GetPeerUserId)(client);
				if (peerUid is null || peerUid != UserId)
					return;

				var request = await ReadRequestAsync(stream, timeout.Token).ConfigureAwait(false);
				if (request is not null)
				{
					Post(request);
					await stream.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
				}
			}
			catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidDataException or SocketException)
			{
				// A broken client must not take the listener down
			}
		}

		private static async Task<bool> TrySendAsync(string path, InstanceRequest request, CancellationToken cancellationToken)
		{
			try
			{
				using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				timeout.CancelAfter(ForwardTimeout);

				await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), timeout.Token).ConfigureAwait(false);
				using var stream = new NetworkStream(socket, ownsSocket: false);

				var buffer = new MemoryStream();
				using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
				{
					writer.Write((int)request.Kind);
					writer.Write(request.WorkingDirectory);
					writer.Write(request.Arguments.Count);
					foreach (var argument in request.Arguments)
						writer.Write(argument);
				}

				await stream.WriteAsync(buffer.ToArray(), timeout.Token).ConfigureAwait(false);

				// Wait for the acknowledgement so we know the primary took it
				var ack = new byte[1];
				return await stream.ReadAsync(ack, timeout.Token).ConfigureAwait(false) == 1;
			}
			catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
			{
				cancellationToken.ThrowIfCancellationRequested();
				return false;
			}
		}

		private static async Task<InstanceRequest?> ReadRequestAsync(Stream stream, CancellationToken token)
		{
			const int MaxArguments = 1024;
			const int MaxStringBytes = 8192;
			const int MaxTotalBytes = 256 * 1024;
			var total = 0;
			var header = new byte[4];

			async Task<int> ReadInt()
			{
				await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
				return BitConverter.ToInt32(header);
			}

			async Task<string> ReadString()
			{
				// BinaryWriter writes a 7-bit encoded length prefix
				var length = 0;
				var shift = 0;
				var one = new byte[1];
				while (true)
				{
					await stream.ReadExactlyAsync(one, token).ConfigureAwait(false);
					length |= (one[0] & 0x7F) << shift;
					if ((one[0] & 0x80) == 0)
						break;

					shift += 7;
					if (shift > 28)
						throw new InvalidDataException();
				}

				if (length is < 0 or > MaxStringBytes || (total += length) > MaxTotalBytes)
					throw new InvalidDataException();

				var bytes = new byte[length];
				await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
				return Encoding.UTF8.GetString(bytes);
			}

			var kind = await ReadInt().ConfigureAwait(false);
			var cwd = await ReadString().ConfigureAwait(false);
			var count = await ReadInt().ConfigureAwait(false);
			if (count is < 0 or > MaxArguments)
				return null;

			var args = new string[count];
			for (var i = 0; i < count; i++)
				args[i] = await ReadString().ConfigureAwait(false);

			// Forwarded arguments are untrusted data: they are only ever parsed as paths and known flags by the app, never executed
			if (args.Any(a => a.Contains('\0')) || cwd.Contains('\0'))
				return null;

			return new InstanceRequest(Enum.IsDefined((InstanceRequestKind)kind) ? (InstanceRequestKind)kind : InstanceRequestKind.CommandLine, cwd, args);
		}
	}
}
