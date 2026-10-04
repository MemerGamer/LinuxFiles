// Copyright (c) Files Community
// Licensed under the MIT License.

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

		[STAThread]
		public static int Main(string[] args)
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

			// Selawik weights (Regular/Semibold/Bold/Light) are matched by family name + weight through fontconfig
			Files.Platform.Linux.Native.FontConfigNative.RegisterFontDirectory(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Linux"));

			// TODO: single instance via ISingleInstanceService (D-Bus or Unix socket); forward args to the running instance

			var host = UnoPlatformHostBuilder.Create()
				.App(() => new App())
				.UseX11()
				.Build();

			host.Run();

			return 0;
		}
	}
}
