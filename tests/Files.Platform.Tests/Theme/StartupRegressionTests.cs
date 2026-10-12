// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.DBus;
using Files.Platform.Linux.Theme;
using Files.Platform.Tests.SystemIntegration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Reflection;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Files.Platform.Tests.Theme
{
	[TestClass]
	[DoNotParallelize]
	public sealed class StartupRegressionTests
	{
		[TestMethod]
		[DataRow(false)]
		[DataRow(true)]
		public async Task UnavailableBus_RetriesSlowConnectionAndDiscardsStaleReconciliation(bool sendNewerSignal)
		{
			using var bus = PrivateBus.Start();
			await using var proxy = new SlowBusProxy(bus.Address);
			using var server = await DBusSession.TryConnectAsync(bus.Address, TimeSpan.FromSeconds(2))
				?? throw new AssertFailedException("no private bus");
			var settings = new SlowSettingsServer(server, sendNewerSignal);
			server.AddMethodHandler(settings);
			Assert.IsTrue(await server.TryRequestNameAsync("org.freedesktop.portal.Desktop", RequestNameOptions.None));

			var address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
			var forced = Environment.GetEnvironmentVariable("FILES_COLOR_SCHEME");
			var gtk = Environment.GetEnvironmentVariable("GTK_THEME");
			var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			settings.SignalObserved = changed.Task;
			void OnChanged(object? sender, EventArgs args) => changed.TrySetResult();
			LinuxColorScheme.Changed += OnChanged;
			try
			{
				Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", "unix:path=/nonexistent/linuxfiles-theme-bus");
				Environment.SetEnvironmentVariable("FILES_COLOR_SCHEME", null);
				Environment.SetEnvironmentVariable("GTK_THEME", "Adwaita:light");
				Assert.IsFalse(LinuxColorScheme.Initialize(), "The startup budget must return the local fallback.");
				await Task.Delay(350);
				Assert.IsFalse((bool)typeof(LinuxColorScheme).GetField("_watching", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!,
					"A failed connection must not mark the watcher subscribed.");
				Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", proxy.Address);
				await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
				await settings.Replied.Task.WaitAsync(TimeSpan.FromSeconds(5));
				await Task.Delay(100);
				Assert.IsTrue(LinuxColorScheme.IsDark, "The preference must reconcile unless a newer signal superseded the read.");
			}
			finally
			{
				LinuxColorScheme.Changed -= OnChanged;
				var connectionField = typeof(LinuxColorScheme).GetField("_connection", BindingFlags.NonPublic | BindingFlags.Static)!;
				(connectionField.GetValue(null) as IDisposable)?.Dispose();
				connectionField.SetValue(null, null);
				typeof(LinuxColorScheme).GetField("_watching", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, false);
				Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", address);
				Environment.SetEnvironmentVariable("FILES_COLOR_SCHEME", forced);
				Environment.SetEnvironmentVariable("GTK_THEME", gtk);
			}
		}

		[TestMethod]
		public void DelayedPortalResult_DoesNotOverwriteNewerUpdateEvenWhenSignalKeepsSameTheme()
		{
			var type = typeof(LinuxColorScheme);
			var version = type.GetField("_updateVersion", BindingFlags.NonPublic | BindingFlags.Static)!;
			var preference = type.GetField("_isDark", BindingFlags.NonPublic | BindingFlags.Static)!;
			var apply = type.GetMethod("ApplyPortalValue", BindingFlags.NonPublic | BindingFlags.Static)!;
			var savedVersion = version.GetValue(null);
			var savedPreference = preference.GetValue(null);
			var forced = Environment.GetEnvironmentVariable("FILES_COLOR_SCHEME");
			try
			{
				Environment.SetEnvironmentVariable("FILES_COLOR_SCHEME", null);
				preference.SetValue(null, true);
				version.SetValue(null, 2L);
				apply.Invoke(null, [1u, 2L]); // New signal, including a no-op update.
				apply.Invoke(null, [2u, 1L]); // Delayed read captured before the signal.
				Assert.IsTrue(LinuxColorScheme.IsDark);
				version.SetValue(null, 3L);
				apply.Invoke(null, [2u, 3L]);
				Assert.IsFalse(LinuxColorScheme.IsDark, "A current update must still be applied.");
			}
			finally
			{
				version.SetValue(null, savedVersion);
				preference.SetValue(null, savedPreference);
				Environment.SetEnvironmentVariable("FILES_COLOR_SCHEME", forced);
			}
		}

		private sealed class SlowBusProxy : IAsyncDisposable
		{
			private readonly Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			private readonly CancellationTokenSource stop = new();
			private readonly string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "theme-bus-" + Guid.NewGuid().ToString("N"));
			private readonly Task pump;
			public string Address => "unix:path=" + path;

			public SlowBusProxy(string target)
			{
				listener.Bind(new UnixDomainSocketEndPoint(path));
				listener.Listen(4);
				pump = ForwardAsync(target.Split(',')[0]["unix:path=".Length..]);
			}

			private async Task ForwardAsync(string target)
			{
				try
				{
					using var incoming = await listener.AcceptAsync(stop.Token);
					// Authentication takes longer than the synchronous startup budget.
					await Task.Delay(400, stop.Token);
					using var outgoing = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
					await outgoing.ConnectAsync(new UnixDomainSocketEndPoint(target), stop.Token);
					using var input = new NetworkStream(incoming);
					using var output = new NetworkStream(outgoing);
					await Task.WhenAll(input.CopyToAsync(output, stop.Token), output.CopyToAsync(input, stop.Token));
				}
				catch (OperationCanceledException) { }
			}

			public async ValueTask DisposeAsync()
			{
				await stop.CancelAsync();
				await pump;
				listener.Dispose();
				stop.Dispose();
				File.Delete(path);
			}
		}

		private sealed class SlowSettingsServer(DBusConnection connection, bool sendNewerSignal) : IPathMethodHandler
		{
			public Task SignalObserved { get; set; } = Task.CompletedTask;
			public TaskCompletionSource Replied { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
				if (sendNewerSignal)
				{
					{
						using var signal = connection.GetMessageWriter();
						signal.WriteSignalHeader(null, Path, "org.freedesktop.portal.Settings", "SettingChanged", "ssv");
						signal.WriteString("org.freedesktop.appearance");
						signal.WriteString("color-scheme");
						signal.WriteVariantUInt32(1);
						connection.TrySendMessage(signal.CreateMessage());
					}
					await SignalObserved.WaitAsync(TimeSpan.FromSeconds(5));
				}
				using var writer = context.CreateReplyWriter("v");
				writer.WriteVariantUInt32(sendNewerSignal ? 2u : 1u);
				context.Reply(writer.CreateMessage());
				Replied.TrySetResult();
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
