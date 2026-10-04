// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Files.Platform.Linux.DBus
{
	/// <summary>
	/// Connects to the session bus. Passing an explicit address keeps tests off the user's real session bus.
	/// </summary>
	public static class DBusSession
	{
		/// <summary>
		/// Connects to the bus at <paramref name="address"/> (default: <c>$DBUS_SESSION_BUS_ADDRESS</c>). Returns null if there is no bus or it cannot be reached in time.
		/// </summary>
		public static async Task<DBusConnection?> TryConnectAsync(string? address, TimeSpan timeout)
		{
			address ??= DBusAddress.Session;
			if (string.IsNullOrEmpty(address))
				return null;

			var connection = new DBusConnection(address);
			try
			{
				await connection.ConnectAsync().AsTask().WaitAsync(timeout).ConfigureAwait(false);
				return connection;
			}
			catch (Exception ex) when (ex is TimeoutException or DBusExceptionBase or System.IO.IOException or System.Net.Sockets.SocketException or ArgumentException or FormatException or NotSupportedException)
			{
				connection.Dispose();
				return null;
			}
		}
	}
}
