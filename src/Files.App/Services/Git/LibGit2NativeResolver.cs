// Copyright (c) Files Community
// Licensed under the MIT License.

using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Files.App.Services.Git
{
	/// <summary>
	/// The project-local LibGit2Sharp package only ships Windows native binaries (<c>git2-&lt;hash&gt;.dll</c>). On Linux the native library is
	/// resolved from a bundled <c>runtimes/linux-*/native</c> copy first and from the system <c>libgit2</c> otherwise.
	/// </summary>
	internal static class LibGit2NativeResolver
	{
		private static readonly string[] SystemNames =
		[
			"libgit2.so.1.9", "libgit2.so.1.8", "libgit2.so.1.7", "libgit2.so.1.6", "libgit2.so",
		];

		/// <summary>
		/// Gets whether a libgit2 native library could be loaded.
		/// </summary>
		public static bool IsAvailable { get; private set; }

		[ModuleInitializer]
		internal static void Register()
		{
			if (!OperatingSystem.IsLinux())
				return;

			try
			{
				NativeLibrary.SetDllImportResolver(typeof(Repository).Assembly, Resolve);
			}
			catch (InvalidOperationException)
			{
				// A resolver is already registered for the assembly
			}
		}

		private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
		{
			if (!libraryName.StartsWith("git2", StringComparison.Ordinal))
				return IntPtr.Zero;

			var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
			var native = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native");
			if (Directory.Exists(native))
			{
				foreach (var file in Directory.EnumerateFiles(native, "libgit2*.so*"))
				{
					if (NativeLibrary.TryLoad(file, out var bundled))
					{
						IsAvailable = true;
						return bundled;
					}
				}
			}

			foreach (var name in SystemNames)
			{
				if (NativeLibrary.TryLoad(name, assembly, searchPath, out var handle))
				{
					IsAvailable = true;
					return handle;
				}
			}

			return IntPtr.Zero;
		}
	}
}
