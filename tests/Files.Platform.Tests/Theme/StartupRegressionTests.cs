// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.DBus;
using Files.Platform.Linux.Theme;
using Files.Platform.Tests.SystemIntegration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Files.Platform.Tests.Theme
{
	[TestClass]
	[DoNotParallelize]
	public sealed class StartupRegressionTests
	{
		[TestMethod]
		public async Task SlowPortal_ReconcilesDarkPreferenceWithoutASettingChangedSignal()
		{
			using var bus = PrivateBus.Start();
			using var server = await DBusSession.TryConnectAsync(bus.Address, TimeSpan.FromSeconds(2))
				?? throw new AssertFailedException("no private bus");
			server.AddMethodHandler(new SlowSettingsServer());
			Assert.IsTrue(await server.TryRequestNameAsync("org.freedesktop.portal.Desktop", RequestNameOptions.None));

			var address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
			var forced = Environment.GetEnvironmentVariable("FILES_COLOR_SCHEME");
			var gtk = Environment.GetEnvironmentVariable("GTK_THEME");
			var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			void OnChanged(object? sender, EventArgs args) => changed.TrySetResult();
			LinuxColorScheme.Changed += OnChanged;
			try
			{
				Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", bus.Address);
				Environment.SetEnvironmentVariable("FILES_COLOR_SCHEME", null);
				Environment.SetEnvironmentVariable("GTK_THEME", "Adwaita:light");
				Assert.IsFalse(LinuxColorScheme.Initialize(), "The startup budget must return the local fallback.");
				await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
				Assert.IsTrue(LinuxColorScheme.IsDark, "The delayed portal preference must replace the fallback.");
			}
			finally
			{
				LinuxColorScheme.Changed -= OnChanged;
				Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", address);
				Environment.SetEnvironmentVariable("FILES_COLOR_SCHEME", forced);
				Environment.SetEnvironmentVariable("GTK_THEME", gtk);
			}
		}

		private sealed class SlowSettingsServer : IPathMethodHandler
		{
			public string Path => "/org/freedesktop/portal/desktop";
			public bool HandlesChildPaths => false;

			public async ValueTask HandleMethodAsync(MethodContext context)
			{
				if (context.Request.MemberAsString != "Read")
				{
					context.ReplyUnknownMethodError();
					return;
				}

				await Task.Delay(400);
				using var writer = context.CreateReplyWriter("v");
				writer.WriteVariantUInt32(1);
				context.Reply(writer.CreateMessage());
			}
		}

		[TestMethod]
		public void SessionPersistence_IsGuardedBeforeReadingOrWritingSettings()
		{
			// The desktop app cannot be loaded by the platform runner; verify its integration boundary.
			var root = new DirectoryInfo(AppContext.BaseDirectory);
			while (root is not null && !File.Exists(System.IO.Path.Combine(root.FullName, "src/Files.App/Platforms/Desktop/App.Desktop.cs")))
				root = root.Parent;
			Assert.IsNotNull(root, "Repository source is required for the desktop integration check.");
			var source = File.ReadAllText(System.IO.Path.Combine(root.FullName, "src/Files.App/Helpers/Application/AppLifecycleHelper.cs"));
			var method = source[source.IndexOf("public static void SaveSessionTabs()", StringComparison.Ordinal)..];
			var settings = method.IndexOf("var userSettingsService", StringComparison.Ordinal);
			var guard = method.IndexOf("if (!MainPageViewModel.StartupTabsLoadedTask.IsCompletedSuccessfully)", StringComparison.Ordinal);
			Assert.IsTrue(guard >= 0 && guard < settings, "Closing before restoration must preserve the saved session.");
			Assert.IsTrue(method[guard..settings].Contains("return;", StringComparison.Ordinal));
		}
	}
}
