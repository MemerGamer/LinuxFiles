// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Clipboard;
using Files.Platform.Linux.Clipboard;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Clipboard
{
	/// <summary>
	/// Exercises the X11 selection owner against a private Xvfb server: one service owns the clipboard, a second one (a separate X
	/// connection, like another application) reads it.
	/// </summary>
	[TestClass]
	public sealed partial class X11ClipboardIntegrationTests
	{
		// Environment.SetEnvironmentVariable does not reach the native environment libX11 reads.
		[System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf8)]
		private static partial int SetNativeEnv(string name, string value, int overwrite);

		[System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "unsetenv", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf8)]
		private static partial int UnsetNativeEnv(string name);

		private static Process? s_xvfb;
		private static string? s_previousDisplay;

		[ClassInitialize]
		public static void StartXvfb(TestContext _)
		{
			if (!File.Exists("/usr/bin/Xvfb") && !File.Exists("/usr/local/bin/Xvfb"))
				return;

			for (var display = 190; display < 230; display++)
			{
				if (File.Exists($"/tmp/.X11-unix/X{display}") || File.Exists($"/tmp/.X{display}-lock"))
					continue;

				var start = new ProcessStartInfo("Xvfb");
				start.ArgumentList.Add($":{display}");
				start.ArgumentList.Add("-nolisten");
				start.ArgumentList.Add("tcp");
				start.ArgumentList.Add("-screen");
				start.ArgumentList.Add("0");
				start.ArgumentList.Add("320x200x24");

				s_xvfb = Process.Start(start);
				for (var i = 0; i < 50 && !File.Exists($"/tmp/.X11-unix/X{display}"); i++)
					Thread.Sleep(100);

				if (!File.Exists($"/tmp/.X11-unix/X{display}"))
				{
					StopXvfb();
					return;
				}

				s_previousDisplay = Environment.GetEnvironmentVariable("DISPLAY");
				Environment.SetEnvironmentVariable("DISPLAY", $":{display}");
				SetNativeEnv("DISPLAY", $":{display}", 1);
				return;
			}
		}

		[ClassCleanup]
		public static void StopXvfb()
		{
			if (s_xvfb is null)
				return;

			Environment.SetEnvironmentVariable("DISPLAY", s_previousDisplay);
			if (s_previousDisplay is null)
				UnsetNativeEnv("DISPLAY");
			else
				SetNativeEnv("DISPLAY", s_previousDisplay, 1);
			try
			{
				s_xvfb.Kill();
				s_xvfb.WaitForExit(3000);
			}
			catch (InvalidOperationException)
			{
			}

			s_xvfb.Dispose();
			s_xvfb = null;
		}

		private static string Diagnose(LinuxClipboardService service)
			=> $"clipboard set failed (DISPLAY={Environment.GetEnvironmentVariable("DISPLAY")}, available={service.IsAvailable}, xvfbExited={s_xvfb?.HasExited})";

		private static void RequireXvfb()
		{
			if (s_xvfb is null)
				Assert.Inconclusive("Xvfb is not installed");
		}

		[TestMethod]
		public async Task CopyFromOneClient_IsReadByAnother()
		{
			RequireXvfb();
			using var owner = new LinuxClipboardService();
			using var reader = new LinuxClipboardService();

			Assert.IsTrue(await owner.SetFilesAsync(["/tmp/a b.txt", "/home/u/árvíz#1"], ClipboardOperation.Copy), Diagnose(owner));
			var read = await reader.GetFilesAsync();

			Assert.IsNotNull(read);
			Assert.AreEqual(ClipboardOperation.Copy, read.Operation);
			CollectionAssert.AreEqual(new[] { "/tmp/a b.txt", "/home/u/árvíz#1" }, read.Paths.ToArray());
		}

		[TestMethod]
		public async Task Cut_IsReadAsCut()
		{
			RequireXvfb();
			using var owner = new LinuxClipboardService();
			using var reader = new LinuxClipboardService();

			Assert.IsTrue(await owner.SetFilesAsync(["/tmp/x"], ClipboardOperation.Cut), Diagnose(owner));
			var read = await reader.GetFilesAsync();

			Assert.AreEqual(ClipboardOperation.Cut, read!.Operation);
		}

		[TestMethod]
		public async Task LargeLists_UseIncrTransfers()
		{
			RequireXvfb();
			using var owner = new LinuxClipboardService(incrThreshold: 2048);
			using var reader = new LinuxClipboardService();

			var paths = Enumerable.Range(0, 3000).Select(i => $"/data/folder {i}/file-{i}.bin").ToArray();
			Assert.IsTrue(await owner.SetFilesAsync(paths, ClipboardOperation.Copy), Diagnose(owner));
			var read = await reader.GetFilesAsync();

			Assert.IsNotNull(read);
			CollectionAssert.AreEqual(paths, read.Paths.ToArray());
		}

		[TestMethod]
		public async Task NewOwner_ReplacesContentAndNotifiesPreviousOwner()
		{
			RequireXvfb();
			using var first = new LinuxClipboardService();
			using var second = new LinuxClipboardService();
			var notified = new TaskCompletionSource();
			await first.SetFilesAsync(["/one"], ClipboardOperation.Copy);
			first.ContentChanged += (_, _) => notified.TrySetResult();

			Assert.IsTrue(await second.SetFilesAsync(["/two"], ClipboardOperation.Cut), Diagnose(second));

			await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
			var read = await first.GetFilesAsync();
			CollectionAssert.AreEqual(new[] { "/two" }, read!.Paths.ToArray());
			Assert.AreEqual(ClipboardOperation.Cut, read.Operation);
		}

		[TestMethod]
		public async Task Clear_ReleasesTheClipboard()
		{
			RequireXvfb();
			using var owner = new LinuxClipboardService();
			using var reader = new LinuxClipboardService();
			await owner.SetFilesAsync(["/tmp/x"], ClipboardOperation.Copy);

			await owner.ClearAsync();

			Assert.IsNull(await reader.GetFilesAsync());
		}

		[TestMethod]
		public async Task RelativeAndEmptyPaths_AreRejected()
		{
			RequireXvfb();
			using var owner = new LinuxClipboardService();

			Assert.IsFalse(await owner.SetFilesAsync(["relative/path", ""], ClipboardOperation.Copy));
		}

		[TestMethod]
		public async Task DragWithoutPressedButton_EndsWithoutOutcome()
		{
			RequireXvfb();
			using var source = new LinuxClipboardService();

			var outcome = await source.DragFilesAsync(["/tmp/x"]).WaitAsync(TimeSpan.FromSeconds(10));

			Assert.AreEqual(FileDragOutcome.None, outcome);
		}
	}
}
