// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Previews;
using System.Threading.Tasks;

namespace Files.Platform.Tests
{
	internal static class Program
	{
		public static Task<int> Main(string[] args)
		{
			if (HtmlSnapshotHelper.TryRunHelper(args, out var exitCode)) return Task.FromResult(exitCode);
			return MicrosoftTestingPlatformApplication.RunAsync(args);
		}
	}
}
