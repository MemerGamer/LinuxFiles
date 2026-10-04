// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Notifications;
using Files.Platform.Linux.DBus;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Files.Platform.Linux.Notifications
{
	/// <summary>
	/// Shows notifications through <c>org.freedesktop.Notifications</c> on the session bus.
	/// </summary>
	public sealed class DBusNotificationService : INotificationService, IDisposable
	{
		private const string Service = "org.freedesktop.Notifications";
		private const string Path = "/org/freedesktop/Notifications";
		private const string AppName = "Files";
		private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);

		private readonly string? busAddress;
		private readonly SemaphoreSlim gate = new(1, 1);
		private DBusConnection? connection;

		/// <summary>
		/// Creates the service. <paramref name="busAddress"/> null means the user's session bus.
		/// </summary>
		public DBusNotificationService(string? busAddress = null)
		{
			this.busAddress = busAddress;
		}

		/// <inheritdoc/>
		public async Task<bool> NotifyAsync(string title, string body, CancellationToken cancellationToken = default)
		{
			try
			{
				var bus = await GetConnectionAsync().ConfigureAwait(false);
				if (bus is null)
					return false;

				var message = BuildNotify(bus, title, body);

				await bus.CallMethodAsync(message, static (Message m, object? _) => m.GetBodyReader().ReadUInt32(), null)
					.WaitAsync(CallTimeout, cancellationToken).ConfigureAwait(false);
				return true;
			}
			catch (Exception ex) when (ex is TimeoutException or DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
			{
				// No notification daemon (or it did not answer): nothing to show
				return false;
			}
		}

		private static MessageBuffer BuildNotify(DBusConnection bus, string title, string body)
		{
			using var writer = bus.GetMessageWriter();
			writer.WriteMethodCallHeader(Service, Path, Service, "Notify", "susssasa{sv}i");
			writer.WriteString(AppName);
			writer.WriteUInt32(0);
			writer.WriteString("io.github.memergamer.LinuxFiles");
			writer.WriteString(title);
			writer.WriteString(body);
			writer.WriteArray(Array.Empty<string>());
			writer.WriteDictionary(new Dictionary<string, VariantValue>());
			writer.WriteInt32(-1);

			return writer.CreateMessage();
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			connection?.Dispose();
			gate.Dispose();
		}

		private async Task<DBusConnection?> GetConnectionAsync()
		{
			await gate.WaitAsync().ConfigureAwait(false);
			try
			{
				return connection ??= await DBusSession.TryConnectAsync(busAddress, CallTimeout).ConfigureAwait(false);
			}
			finally
			{
				gate.Release();
			}
		}
	}
}
