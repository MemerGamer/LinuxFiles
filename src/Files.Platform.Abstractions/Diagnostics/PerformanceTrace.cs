// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Diagnostics
{
	/// <summary>Opt-in, bounded performance records without paths, arguments or payloads.</summary>
	public static class PerformanceTrace
	{
		public static bool Enabled { get; } = Environment.GetEnvironmentVariable("FILES_TRACE") == "1";
		private static readonly AsyncLocal<long> current = new();
		private static readonly Channel<Record>? records = Enabled ? CreateChannel() : null;
		private static long nextId;

		private readonly record struct Record(long Id, long Parent, string Operation, double Milliseconds, int Thread, bool? UiThread, string? Outcome);

		private static Channel<Record> CreateChannel()
		{
			var channel = Channel.CreateBounded<Record>(new BoundedChannelOptions(1024)
			{
				FullMode = BoundedChannelFullMode.DropWrite,
				SingleReader = true,
			});
			_ = Task.Run(async () =>
			{
				await foreach (var record in channel.Reader.ReadAllAsync().ConfigureAwait(false))
				{
					try
					{
						Console.Error.WriteLine(FormattableString.Invariant($"[files-trace] id={record.Id} parent={record.Parent} operation={record.Operation} ms={record.Milliseconds:F3} thread={record.Thread} ui={record.UiThread?.ToString() ?? "unknown"} outcome={record.Outcome ?? "completed-or-unwound"}"));
					}
					catch (System.IO.IOException) { }
				}
			});
			return channel;
		}

		public static Scope Begin(string operation, bool? uiThread = null)
		{
			if (!Enabled) return default;
			var parent = current.Value;
			var id = Interlocked.Increment(ref nextId);
			current.Value = id;
			return new Scope(id, parent, operation, uiThread);
		}

		public readonly struct Scope : IDisposable
		{
			private readonly long id, parent, start;
			private readonly string? operation;
			private readonly bool? uiThread;

			internal Scope(long id, long parent, string operation, bool? uiThread)
			{
				this.id = id;
				this.parent = parent;
				this.operation = operation;
				this.uiThread = uiThread;
				start = Stopwatch.GetTimestamp();
			}

			public void Dispose() => Complete(null);

			public void Complete(string? outcome)
			{
				if (operation is null) return;
				current.Value = parent;
				records?.Writer.TryWrite(new Record(id, parent, operation, Stopwatch.GetElapsedTime(start).TotalMilliseconds,
					Environment.CurrentManagedThreadId, uiThread, outcome));
			}
		}
	}
}
