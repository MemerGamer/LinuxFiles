// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Wallpaper;
using Files.Platform.Linux.DBus;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Files.Platform.Linux.Wallpaper
{
	/// <summary>
	/// Sets the wallpaper through <c>org.freedesktop.portal.Wallpaper</c> (xdg-desktop-portal). The desktop shows its own confirmation.
	/// Nothing else is used: without the portal the feature is unavailable.
	/// </summary>
	public sealed class PortalWallpaperService : IWallpaperService, IDisposable
	{
		private const string Service = "org.freedesktop.portal.Desktop";
		private const string ObjectPath = "/org/freedesktop/portal/desktop";
		private const string WallpaperInterface = "org.freedesktop.portal.Wallpaper";
		private const string RequestInterface = "org.freedesktop.portal.Request";
		private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);
		private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromMinutes(10);

		private readonly string? busAddress;
		private readonly SemaphoreSlim gate = new(1, 1);
		private readonly ConcurrentDictionary<string, TaskCompletionSource<uint>> pending = new();
		private DBusConnection? connection;
		private IDisposable? responseSubscription;

		/// <summary>
		/// Creates the service. <paramref name="busAddress"/> null means the user's session bus.
		/// </summary>
		public PortalWallpaperService(string? busAddress = null)
		{
			this.busAddress = busAddress;
		}

		/// <inheritdoc/>
		public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
		{
			try
			{
				var bus = await GetConnectionAsync().ConfigureAwait(false);
				if (bus is null)
					return false;

				// Reading the interface version never changes anything; it also activates a not-yet-running portal
				MessageBuffer message;
				using (var writer = bus.GetMessageWriter())
				{
					writer.WriteMethodCallHeader(Service, ObjectPath, "org.freedesktop.DBus.Properties", "Get", "ss");
					writer.WriteString(WallpaperInterface);
					writer.WriteString("version");
					message = writer.CreateMessage();
				}

				await bus.CallTracedAsync("dbus.wallpaper", message, static (Message _, object? _) => true, null).WaitAsync(CallTimeout, cancellationToken).ConfigureAwait(false);
				return true;
			}
			catch (Exception ex) when (IsBackendFailure(ex))
			{
				return false;
			}
		}

		/// <inheritdoc/>
		public async Task<WallpaperResult> SetAsync(string imagePath, WallpaperTarget target, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrEmpty(imagePath) || !imagePath.StartsWith('/') || imagePath.Contains('\0'))
				return WallpaperResult.Failed;

			try
			{
				var bus = await GetConnectionAsync().ConfigureAwait(false);
				if (bus is null)
					return WallpaperResult.Unavailable;

				var token = "files" + Guid.NewGuid().ToString("N");
				var response = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
				pending[token] = response;

				try
				{
					// The portal receives an open descriptor, so the path is never re-parsed (no URI escaping, no swap after the call)
					using var image = File.OpenHandle(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
					MessageBuffer message;
					using (var writer = bus.GetMessageWriter())
					{
						writer.WriteMethodCallHeader(Service, ObjectPath, WallpaperInterface, "SetWallpaperFile", "sha{sv}");
						writer.WriteString("");
						writer.WriteHandle(image);
						writer.WriteDictionary(new Dictionary<string, VariantValue>
						{
							["handle_token"] = token,
							["show-preview"] = true,
							["set-on"] = target == WallpaperTarget.LockScreen ? "lockscreen" : "background",
						});
						message = writer.CreateMessage();
					}

					await bus.CallTracedAsync("dbus.wallpaper", message, static (Message _, object? _) => true, null).WaitAsync(CallTimeout, cancellationToken).ConfigureAwait(false);

					uint code;
					try
					{
						code = await response.Task.WaitAsync(ConfirmTimeout, cancellationToken).ConfigureAwait(false);
					}
					catch (TimeoutException)
					{
						// The desktop's confirmation may still be open; do not claim failure
						return WallpaperResult.Cancelled;
					}

					return code switch
					{
						0 => WallpaperResult.Applied,
						1 => WallpaperResult.Cancelled,
						_ => WallpaperResult.Failed,
					};
				}
				finally
				{
					pending.TryRemove(token, out _);
				}
			}
			catch (Exception ex) when (IsBackendFailure(ex))
			{
				return ex is DBusErrorReplyException dbus && dbus.ErrorName == "org.freedesktop.DBus.Error.ServiceUnknown"
					? WallpaperResult.Unavailable
					: WallpaperResult.Failed;
			}
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
					return (Path: m.PathAsString ?? "", Code: reader.ReadUInt32());
				}, static (Exception? ex, (string Path, uint Code) value, object? _, object? state) =>
				{
					if (ex is not null)
						return;

					var self = (PortalWallpaperService)state!;
					var token = value.Path[(value.Path.LastIndexOf('/') + 1)..];
					if (self.pending.TryGetValue(token, out var waiter))
						waiter.TrySetResult(value.Code);
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
