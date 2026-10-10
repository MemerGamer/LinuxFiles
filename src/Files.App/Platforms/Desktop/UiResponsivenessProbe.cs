// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Dispatching;
using System.Diagnostics;
using System.Threading.Channels;

namespace Files.App
{
	/// <summary>
	/// Opt-in (FILES_TRACE=1) probe that reports dispatcher tick lateness on a background worker.
	/// It observes only timing, never content, and avoids CompositionTarget.Rendering, which makes Uno render continuously.
	/// </summary>
	internal static class UiResponsivenessProbe
	{
		private const int TickMs = 10;
		private const double StallThresholdMs = 50;

		private static DispatcherQueueTimer? timer;

		public static void Start(DispatcherQueue queue)
		{
			if (!Files.Platform.Abstractions.Diagnostics.PerformanceTrace.Enabled || timer is not null)
				return;

			StartEnabled(queue);
		}

		private static void StartEnabled(DispatcherQueue queue)
		{
			var records = Channel.CreateBounded<(long Timestamp, double Lateness)>(new BoundedChannelOptions(128)
			{
				FullMode = BoundedChannelFullMode.DropWrite,
				SingleReader = true,
				SingleWriter = true,
				AllowSynchronousContinuations = false,
			});
			_ = Task.Run(async () =>
			{
				await foreach (var record in records.Reader.ReadAllAsync().ConfigureAwait(false))
				{
					try
					{
						Console.Error.WriteLine(FormattableString.Invariant($"[files-ui] t={record.Timestamp} stall-ms={record.Lateness:F0}"));
					}
					catch (System.IO.IOException) { }
					catch (ObjectDisposedException) { }
				}
			});

			var last = Stopwatch.GetTimestamp();
			timer = queue.CreateTimer();
			timer.Interval = TimeSpan.FromMilliseconds(TickMs);
			timer.IsRepeating = true;
			timer.Tick += (_, _) =>
			{
				var now = Stopwatch.GetTimestamp();
				var lateness = Stopwatch.GetElapsedTime(last, now).TotalMilliseconds - TickMs;
				last = now;
				if (lateness > StallThresholdMs)
					records.Writer.TryWrite((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), lateness));
			};
			timer.Start();
		}
	}
}
