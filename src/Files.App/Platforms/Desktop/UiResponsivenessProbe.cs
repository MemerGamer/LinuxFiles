// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Dispatching;
using System.Diagnostics;

namespace Files.App
{
	/// <summary>
	/// Opt-in (FILES_TRACE=1) probe that reports UI-thread dispatcher stalls: a 10 ms timer logs
	/// whenever its ticks arrive late. It observes only timing, never content. It deliberately does not
	/// subscribe to CompositionTarget.Rendering, which makes Uno render continuously.
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

			var last = Stopwatch.GetTimestamp();
			timer = queue.CreateTimer();
			timer.Interval = TimeSpan.FromMilliseconds(TickMs);
			timer.IsRepeating = true;
			timer.Tick += (_, _) =>
			{
				var now = Stopwatch.GetTimestamp();
				var gap = Stopwatch.GetElapsedTime(last, now).TotalMilliseconds;
				last = now;
				if (gap > StallThresholdMs)
					Console.Error.WriteLine(FormattableString.Invariant($"[files-ui] t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()} stall-ms={gap:F0}"));
			};
			timer.Start();
		}
	}
}
