// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Instance;
using Files.Platform.Linux.Instance;
using Files.Platform.Linux.Elevation;
using System.Text;
using Uno.UI.Hosting;

namespace Files.App
{
	/// <summary>
	/// Represents the entry point of the Files app on Uno Platform (Skia desktop, X11).
	/// Replaces <c>Program.cs</c>, which is built for Windows only (WinAppSDK AppInstance redirection).
	/// </summary>
	internal sealed class Program
	{
		/// <summary>
		/// Gets or sets the semaphore a parked (background) instance waits on. Unused on desktop for now.
		/// </summary>
		public static Semaphore? Pool { get; set; }

		/// <summary>
		/// Returns the working directory the app was launched from. TODO: capture it for redirected activations.
		/// </summary>
		public static string ConsumeLaunchCwd() => Environment.CurrentDirectory;

		/// <summary>
		/// Gets the command line arguments (without the program name) this process was started with.
		/// </summary>
		public static string[] LaunchArguments { get; private set; } = [];

		/// <summary>
		/// Gets the single instance service this process owns. Null when single instance handling is disabled.
		/// </summary>
		internal static LinuxSingleInstanceService? SingleInstance { get; private set; }

		[STAThread]
		public static int Main(string[] args)
		{
			try
			{
				RootStartupEnvironment.Apply();
			}
			catch (System.IO.IOException)
			{
				Console.Error.WriteLine("[Files] Refusing root startup: root's account and settings directories must be exclusively controlled by root.");
				return 1;
			}
			RootActionsAvailability.Configure(args);

			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

			// Selawik weights (Regular/Semibold/Bold/Light) are matched by family name + weight through fontconfig
			Files.Platform.Linux.Native.FontConfigNative.RegisterFontDirectory(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Linux"));

			LaunchArguments = args;

			// Uno reads Xft.dpi itself; this covers sessions that only export GDK/Qt scale variables
			Files.Platform.Linux.Windowing.DisplayScaleResolver.ApplyToProcess();

			// Root-mode launches use a separate process so they never change or forward into an ordinary window.
			// Ordinary second launches forward through D-Bus (socket fallback).
			// FILES_NO_SINGLE_INSTANCE=1 skips this (for running several instances side by side while developing).
			var noSingleInstance = !RootActionsAvailability.Mode.UseSingleInstance || Environment.GetEnvironmentVariable("FILES_NO_SINGLE_INSTANCE") == "1";

			// Keep the flag from leaking into apps and terminals launched from this instance
			Environment.SetEnvironmentVariable("FILES_NO_SINGLE_INSTANCE", null);

			if (!noSingleInstance)
			{
				var singleInstance = new LinuxSingleInstanceService();
				var request = new InstanceRequest(InstanceRequestKind.CommandLine, Environment.CurrentDirectory, args);

				bool isPrimary;
				try
				{
					isPrimary = singleInstance.TryBecomePrimaryAsync(request).GetAwaiter().GetResult();
				}
				catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or System.Net.Sockets.SocketException or UnauthorizedAccessException)
				{
					Console.Error.WriteLine($"[Files] Single instance check failed, continuing: {ex.Message}");
					isPrimary = true;
				}

				if (!isPrimary)
				{
					Console.Error.WriteLine("[Files] Forwarded the command line to the running instance.");
					return 0;
				}

				SingleInstance = singleInstance;
			}

			var host = UnoPlatformHostBuilder.Create()
				.App(() => new App())
				.UseX11()
				.Build();

			host.Run();

			SingleInstance?.DisposeAsync().AsTask().GetAwaiter().GetResult();

			return 0;
		}
	}
}
