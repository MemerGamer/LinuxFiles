// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.FileChooser;
using Files.Platform.Linux.DBus;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Files.Platform.Linux.FileChooser
{
	/// <summary>
	/// Shows the chooser through <c>org.freedesktop.portal.FileChooser</c> (xdg-desktop-portal).
	/// Without the portal every request reports <see cref="FileChooserStatus.Unavailable"/>.
	/// </summary>
	public sealed class PortalFileChooserService : IFileChooserService, IDisposable
	{
		private const string Service = "org.freedesktop.portal.Desktop";
		private const string ObjectPath = "/org/freedesktop/portal/desktop";
		private const string FileChooserInterface = "org.freedesktop.portal.FileChooser";
		private const string RequestInterface = "org.freedesktop.portal.Request";
		private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);
		private static readonly TimeSpan ChooseTimeout = TimeSpan.FromHours(1);

		private readonly string? busAddress;
		private readonly SemaphoreSlim gate = new(1, 1);
		private readonly ConcurrentDictionary<string, TaskCompletionSource<(uint Code, string[] Uris)>> pending = new();
		private DBusConnection? connection;
		private IDisposable? responseSubscription;

		/// <summary>
		/// Creates the service. <paramref name="busAddress"/> null means the user's session bus.
		/// </summary>
		public PortalFileChooserService(string? busAddress = null)
		{
			this.busAddress = busAddress;
		}

		/// <inheritdoc/>
		public async Task<FileChooserResult> ChooseAsync(FileChooserRequest request, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string? handlePath = null;
			DBusConnection? bus = null;
			try
			{
				bus = await GetConnectionAsync().ConfigureAwait(false);
				if (bus is null)
					return FileChooserResult.Unavailable;

				var token = "files" + Guid.NewGuid().ToString("N");
				var response = new TaskCompletionSource<(uint Code, string[] Uris)>(TaskCreationOptions.RunContinuationsAsynchronously);
				pending[token] = response;

				try
				{
					var parent = request.ParentWindowId == 0 ? string.Empty : "x11:" + request.ParentWindowId.ToString("x", CultureInfo.InvariantCulture);
					MessageBuffer message;
					var writer = bus.GetMessageWriter();
					try
					{
						writer.WriteMethodCallHeader(Service, ObjectPath, FileChooserInterface, request.Save && !request.PickFolder ? "SaveFile" : "OpenFile", "ssa{sv}");
						writer.WriteString(parent);
						writer.WriteString(request.Title ?? string.Empty);
						WriteOptions(ref writer, request, token);
						message = writer.CreateMessage();
					}
					finally
					{
						writer.Dispose();
					}

					handlePath = await bus.CallMethodAsync(message, static (Message m, object? _) => m.GetBodyReader().ReadObjectPathAsString(), null).WaitAsync(CallTimeout, cancellationToken).ConfigureAwait(false);

					// Older portals may not honor handle_token; the returned handle is authoritative
					var returnedToken = handlePath[(handlePath.LastIndexOf('/') + 1)..];
					if (returnedToken.Length > 0 && returnedToken != token)
						pending[returnedToken] = response;

					var (code, uris) = await response.Task.WaitAsync(ChooseTimeout, cancellationToken).ConfigureAwait(false);
					if (code != 0)
						return FileChooserResult.Cancelled;

					var paths = new List<string>();
					foreach (var uri in uris)
					{
						if (TryGetLocalPath(uri, out var path))
							paths.Add(path);
					}

					return paths.Count == 0 ? FileChooserResult.Cancelled : new FileChooserResult(FileChooserStatus.Selected, paths);
				}
				finally
				{
					pending.TryRemove(token, out _);
					if (handlePath is not null)
						pending.TryRemove(handlePath[(handlePath.LastIndexOf('/') + 1)..], out _);

					// An unanswered request means the dialog may still be open; ask the portal to close it
					if (handlePath is not null && !response.Task.IsCompleted)
						CloseRequest(bus, handlePath);
				}
			}
			catch (Exception ex) when (IsBackendFailure(ex))
			{
				return ex is DBusErrorReplyException dbus && dbus.ErrorName is "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.UnknownMethod" or "org.freedesktop.DBus.Error.UnknownObject"
					? FileChooserResult.Unavailable
					: FileChooserResult.Cancelled;
			}
		}

		private static void CloseRequest(DBusConnection bus, string handlePath)
		{
			try
			{
				using var writer = bus.GetMessageWriter();
				writer.WriteMethodCallHeader(Service, handlePath, RequestInterface, "Close");
				bus.TrySendMessage(writer.CreateMessage());
			}
			catch (Exception ex) when (IsBackendFailure(ex))
			{
			}
		}

		private static void WriteOptions(ref MessageWriter writer, FileChooserRequest request, string token)
		{
			var start = writer.WriteDictionaryStart();

			writer.WriteDictionaryEntryStart();
			writer.WriteString("handle_token");
			writer.WriteVariantString(token);

			if (!request.Save)
			{
				writer.WriteDictionaryEntryStart();
				writer.WriteString("multiple");
				writer.WriteVariantBool(request.Multiple && !request.PickFolder);
			}

			if (request.PickFolder)
			{
				writer.WriteDictionaryEntryStart();
				writer.WriteString("directory");
				writer.WriteVariantBool(true);
			}
			else
			{
				if (request.Filters.Count > 0)
				{
					writer.WriteDictionaryEntryStart();
					writer.WriteString("filters");
					writer.WriteSignature("a(sa(us))");
					var filters = writer.WriteArrayStart(DBusType.Struct);
					foreach (var filter in request.Filters)
					{
						writer.WriteStructureStart();
						writer.WriteString(filter.Name);
						var globs = writer.WriteArrayStart(DBusType.Struct);
						foreach (var pattern in filter.Patterns)
						{
							writer.WriteStructureStart();
							writer.WriteUInt32(0);
							writer.WriteString(pattern);
						}

						writer.WriteArrayEnd(globs);
					}

					writer.WriteArrayEnd(filters);
				}

				if (request.Save && !string.IsNullOrEmpty(request.CurrentName))
				{
					writer.WriteDictionaryEntryStart();
					writer.WriteString("current_name");
					writer.WriteVariantString(request.CurrentName);
				}

				if (request.Save && !string.IsNullOrEmpty(request.CurrentFolder) && !request.CurrentFolder.Contains('\0'))
				{
					writer.WriteDictionaryEntryStart();
					writer.WriteString("current_folder");
					writer.WriteSignature("ay");
					writer.WriteArray(Encoding.UTF8.GetBytes(request.CurrentFolder + "\0"));
				}
			}

			writer.WriteDictionaryEnd(start);
		}

		/// <summary>
		/// Decodes a <c>file://</c> URI (empty or <c>localhost</c> authority) to an absolute path; anything else is rejected.
		/// </summary>
		public static bool TryGetLocalPath(string uri, out string path)
		{
			path = string.Empty;
			const string Prefix = "file://";
			if (uri is null || !uri.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
				return false;

			var rest = uri[Prefix.Length..];
			var slash = rest.IndexOf('/');
			if (slash < 0)
				return false;

			var authority = rest[..slash];
			if (authority.Length != 0 && !authority.Equals("localhost", StringComparison.OrdinalIgnoreCase))
				return false;

			var encoded = rest[slash..];
			var cut = encoded.IndexOfAny(['?', '#']);
			if (cut >= 0)
				encoded = encoded[..cut];

			string decoded;
			try
			{
				decoded = Uri.UnescapeDataString(encoded);
			}
			catch (UriFormatException)
			{
				return false;
			}

			if (decoded.Contains('\0'))
				return false;

			path = decoded;
			return true;
		}

		private static bool IsBackendFailure(Exception ex)
			=> ex is TimeoutException or DBusExceptionBase or ObjectDisposedException or InvalidOperationException or IOException or UnauthorizedAccessException;

		private async Task<DBusConnection?> GetConnectionAsync()
		{
			await gate.WaitAsync().ConfigureAwait(false);
			try
			{
				if (connection is not null)
					return connection;

				var bus = await DBusSession.TryConnectAsync(busAddress, CallTimeout).ConfigureAwait(false);
				if (bus is null)
					return null;

				var rule = new MatchRule
				{
					Type = MessageType.Signal,
					Sender = Service,
					Interface = RequestInterface,
					Member = "Response",
				};

				responseSubscription = await bus.AddMatchAsync(rule, static (Message m, object? _) =>
				{
					var reader = m.GetBodyReader();
					var code = reader.ReadUInt32();
					var uris = Array.Empty<string>();
					var results = reader.ReadDictionaryOfStringToVariantValue();
					if (results.TryGetValue("uris", out var value) && value.Type == VariantValueType.Array)
						uris = value.GetArray<string>();

					return (Path: m.PathAsString ?? "", Code: code, Uris: uris);
				}, static (Exception? ex, (string Path, uint Code, string[] Uris) value, object? _, object? state) =>
				{
					if (ex is not null)
						return;

					var self = (PortalFileChooserService)state!;
					var token = value.Path[(value.Path.LastIndexOf('/') + 1)..];
					if (self.pending.TryGetValue(token, out var waiter))
						waiter.TrySetResult((value.Code, value.Uris));
				}, null, this, false, ObserverFlags.None).ConfigureAwait(false);

				connection = bus;
				return bus;
			}
			catch (Exception ex) when (IsBackendFailure(ex))
			{
				return null;
			}
			finally
			{
				gate.Release();
			}
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			responseSubscription?.Dispose();
			connection?.Dispose();
			connection = null;
		}
	}
}
