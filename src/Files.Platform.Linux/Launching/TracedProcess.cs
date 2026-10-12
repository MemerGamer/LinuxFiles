// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Diagnostics;
using Files.Platform.Linux.Elevation;
using Files.Platform.Linux.Native;
using System.Diagnostics;

namespace Files.Platform.Linux.Launching
{
	internal static class TracedProcess
	{
		public static Process? Start(ProcessStartInfo info)
		{
			if (ProcessIdentityNative.CurrentUserId == 0)
				RootStartupEnvironment.ApplyChildEnvironment(info, 0, ElevationNative.HomeDirectory(0));
			using var trace = PerformanceTrace.Begin("process-spawn");
			return Process.Start(info);
		}
	}
}
