// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Native;
using LibGit2Sharp;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Files.App.Services.Git
{
	/// <summary>
	/// The project-local LibGit2Sharp package only ships Windows native binaries (<c>git2-&lt;hash&gt;.dll</c>) and the managed
	/// library itself claims the DllImport resolver of its assembly, so on Linux the library is made available through
	/// <see cref="GlobalSettings.NativeLibraryPath"/>: a <c>git2-&lt;hash&gt;.so</c> link in the cache directory pointing at a bundled
	/// <c>runtimes/linux-*/native/libgit2*.so</c> or at the system <c>libgit2</c> (ABI-compatible 1.7 - 1.9).
	/// </summary>
	internal static partial class LibGit2NativeResolver
	{
		private static readonly string[] SystemCandidates =
		[
			"/usr/lib/libgit2.so.1.9", "/usr/lib64/libgit2.so.1.9", "/usr/lib/x86_64-linux-gnu/libgit2.so.1.9", "/usr/lib/aarch64-linux-gnu/libgit2.so.1.9",
			"/usr/lib/libgit2.so.1.8", "/usr/lib64/libgit2.so.1.8", "/usr/lib/x86_64-linux-gnu/libgit2.so.1.8", "/usr/lib/aarch64-linux-gnu/libgit2.so.1.8",
			"/usr/lib/libgit2.so.1.7", "/usr/lib64/libgit2.so.1.7", "/usr/lib/x86_64-linux-gnu/libgit2.so.1.7", "/usr/lib/aarch64-linux-gnu/libgit2.so.1.7",
			"/usr/lib/libgit2.so", "/usr/lib64/libgit2.so", "/usr/lib/x86_64-linux-gnu/libgit2.so", "/usr/lib/aarch64-linux-gnu/libgit2.so",
		];

		/// <summary>
		/// Gets the library file that was linked, or <see langword="null"/> when none could be found.
		/// </summary>
		public static string? LinkedLibrary { get; private set; }

		[ModuleInitializer]
		internal static void Configure()
		{
			if (!OperatingSystem.IsLinux())
				return;

			try
			{
				var info = typeof(Repository).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
				var match = info is null ? null : NativeNameRegex().Match(info);
				if (match is not { Success: true })
					return;

				var source = FindSource();
				if (source is null)
					return;

				// XDG_CACHE_HOME is only honoured when absolute; the directory chain is verified before anything gets loaded from it
				var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
				var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg)
					? xdg
					: Path.Combine(home, ".cache");
				var directory = TrustedNativeDirectory.Prepare(cache, ["Files", "native"], $"git2-{match.Groups[1].Value}.so", Path.GetFullPath(source));
				if (directory is null)
					return;

				GlobalSettings.NativeLibraryPath = directory;
				LinkedLibrary = source;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
			{
				// Without the native library every Git call fails soft (SafetyExtensions / LibGit2SharpException handling at the call sites)
			}
		}

		private static string? FindSource()
		{
			var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
			var bundled = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native");
			if (Directory.Exists(bundled))
			{
				var file = Directory.EnumerateFiles(bundled, "libgit2*.so*").FirstOrDefault();
				if (file is not null)
					return file;
			}

			return SystemCandidates.FirstOrDefault(File.Exists);
		}

		[GeneratedRegex(@"libgit2-([0-9a-f]+)")]
		private static partial Regex NativeNameRegex();
	}
}
