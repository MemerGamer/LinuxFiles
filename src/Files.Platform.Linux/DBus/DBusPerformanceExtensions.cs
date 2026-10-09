// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Diagnostics;
using System;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Files.Platform.Linux.DBus
{
	internal static class DBusPerformanceExtensions
	{
		public static Task<T> CallTracedAsync<T>(this DBusConnection bus, string operation, MessageBuffer message, MessageValueReader<T> reader, object? state)
			=> PerformanceTrace.Enabled ? TraceAsync(operation, bus.CallMethodAsync(message, reader, state)) : bus.CallMethodAsync(message, reader, state);

		public static Task CallTracedAsync(this DBusConnection bus, string operation, MessageBuffer message)
			=> PerformanceTrace.Enabled ? TraceAsync(operation, bus.CallMethodAsync(message)) : bus.CallMethodAsync(message);

		private static async Task<T> TraceAsync<T>(string operation, Task<T> call)
		{
			var trace = PerformanceTrace.Begin(operation);
			try
			{
				var result = await call.ConfigureAwait(false);
				trace.Complete("reply");
				return result;
			}
			catch
			{
				trace.Complete("fault");
				throw;
			}
		}

		private static async Task TraceAsync(string operation, Task call)
		{
			var trace = PerformanceTrace.Begin(operation);
			try
			{
				await call.ConfigureAwait(false);
				trace.Complete("reply");
			}
			catch
			{
				trace.Complete("fault");
				throw;
			}
		}
	}
}
