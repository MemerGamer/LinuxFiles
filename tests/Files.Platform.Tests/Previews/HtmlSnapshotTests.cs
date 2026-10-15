// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Previews;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Previews
{
	[TestClass]
	public sealed class HtmlSnapshotTests
	{
		[TestMethod]
		[DataRow("/tmp/page.html", "400", "8192", true)]
		[DataRow("relative.html", "400", "600", false)]
		[DataRow("/tmp/page.html", "0", "600", false)]
		[DataRow("/tmp/page.html", "-1", "600", false)]
		[DataRow("/tmp/page.html", "2049", "600", false)]
		[DataRow("/tmp/page.html", "1.5", "600", false)]
		[DataRow("/tmp/page.html", " 400", "600", false)]
		[DataRow("/tmp/page.html", "400", "0", false)]
		[DataRow("/tmp/page.html", "400", "8193", false)]
		[DataRow("/tmp/page.html", "400", "2147483648", false)]
		public void ParsesOnlyBoundedAbsoluteArguments(string path, string width, string height, bool valid)
		{
			Assert.AreEqual(valid, HtmlSnapshotHelper.TryParseArguments([HtmlSnapshotHelper.Switch, path, width, height], out _, out _, out _));
		}

		[TestMethod]
		public void RejectsExtraMissingAndUnrecognizedArguments()
		{
			Assert.IsFalse(HtmlSnapshotHelper.TryParseArguments([HtmlSnapshotHelper.Switch, "/tmp/x", "400", "600", "extra"], out _, out _, out _));
			Assert.IsFalse(HtmlSnapshotHelper.TryParseArguments([HtmlSnapshotHelper.Switch, "/tmp/x", "400"], out _, out _, out _));
			Assert.IsFalse(HtmlSnapshotHelper.TryParseArguments(["--other", "/tmp/x", "400", "600"], out _, out _, out _));
			Assert.IsFalse(HtmlSnapshotHelper.TryRunHelper([], out _));
			Assert.IsFalse(HtmlSnapshotHelper.TryRunHelper(["--other"], out _));
			Assert.IsTrue(HtmlSnapshotHelper.TryRunHelper([HtmlSnapshotHelper.Switch, "relative", "1", "1"], out var code));
			Assert.AreNotEqual(0, code);
		}

		[TestMethod]
		public async Task PngReaderValidatesSignatureAndExactByteCap()
		{
			byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, 42];
			Assert.IsTrue(HtmlSnapshotRenderer.HasPngSignature(png));
			Assert.IsFalse(HtmlSnapshotRenderer.HasPngSignature(png.AsSpan(0, 7)));
			Assert.IsFalse(HtmlSnapshotRenderer.HasPngSignature("not a PNG"u8));
			using var valid = new MemoryStream(png);
			CollectionAssert.AreEqual(png, await HtmlSnapshotRenderer.ReadPngAsync(valid, png.Length, CancellationToken.None));
			using var tooLarge = new MemoryStream(png);
			await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HtmlSnapshotRenderer.ReadPngAsync(tooLarge, png.Length - 1, CancellationToken.None));
			using var invalid = new MemoryStream(new byte[9]);
			await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HtmlSnapshotRenderer.ReadPngAsync(invalid, 10, CancellationToken.None));
			using var empty = new MemoryStream();
			await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HtmlSnapshotRenderer.ReadPngAsync(empty, 10, CancellationToken.None));
		}

		[TestMethod]
		public void TrimsOnlyUniformTrailingRows()
		{
			// 2-pixel BGRA rows; the page background is the top-left pixel.
			byte[] content = [7, 7, 7, 255, 9, 9, 9, 255];
			byte[] background = [7, 7, 7, 255, 7, 7, 7, 255];
			byte[] footer = [0, 0, 255, 255, 0, 0, 255, 255];
			byte[] pixels = [.. content, .. content, .. background, .. background];
			Assert.AreEqual(2 + 1, HtmlSnapshotHelper.TrimUniformBottom(pixels, 8, 8, 4));
			byte[] noisyBottom = [.. content, .. background, .. content];
			Assert.AreEqual(3, HtmlSnapshotHelper.TrimUniformBottom(noisyBottom, 8, 8, 3));
			byte[] blank = [.. background, .. background];
			Assert.AreEqual(1, HtmlSnapshotHelper.TrimUniformBottom(blank, 8, 8, 2));
			byte[] solidFooter = [.. content, .. background, .. footer, .. footer, .. footer];
			Assert.AreEqual(5, HtmlSnapshotHelper.TrimUniformBottom(solidFooter, 8, 8, 5));
			byte[] blankPage = [.. background, .. background, .. background, .. background];
			Assert.AreEqual(2, HtmlSnapshotHelper.TrimUniformBottom(blankPage, 8, 8, 4, minHeight: 2));
		}

		[TestMethod]
		[DataRow("SNAP", "/snap/x")]
		[DataRow("SNAP_NAME", "x")]
		[DataRow("SNAP_REVISION", "1")]
		[DataRow("WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS", "1")]
		public void RefusesEnvironmentsWhereWebKitSkipsTheSandbox(string name, string value)
		{
			Assert.IsFalse(HtmlSnapshotHelper.IsSandboxEnvironmentSupported(n => n == name ? value : null, _ => false));
			Assert.IsTrue(HtmlSnapshotHelper.IsSandboxEnvironmentSupported(_ => null, _ => false));
			Assert.IsFalse(HtmlSnapshotHelper.IsSandboxEnvironmentSupported(_ => null, f => f == "/.flatpak-info"));
		}

		[TestMethod]
		public void LaunchUsesArgumentListAndDotnetEntryDll()
		{
			var start = HtmlSnapshotRenderer.CreateStartInfo("/usr/bin/dotnet", "/tmp/Files.dll", "/tmp/file with ' $quotes.html", 320, 2);
			Assert.IsFalse(start.UseShellExecute);
			CollectionAssert.AreEqual(new[] { "/tmp/Files.dll", HtmlSnapshotHelper.Switch, "/tmp/file with ' $quotes.html", "320", "8192" }, new System.Collections.Generic.List<string>(start.ArgumentList));
			Assert.AreEqual("2", start.Environment[HtmlSnapshotHelper.ScaleVariable]);
			Assert.AreEqual("1", start.Environment["GDK_SCALE"]);
			var native = HtmlSnapshotRenderer.CreateStartInfo("/opt/Files", "/opt/Files", "/tmp/x", 320, 1);
			Assert.AreEqual(HtmlSnapshotHelper.Switch, native.ArgumentList[0]);
		}

		[TestMethod]
		[DataRow(1)]
		[DataRow(2)]
		public async Task RendersRequestedWidthAndDisablesJavaScript(int scale)
		{
			RequireIntegration();
			var root = Directory.CreateTempSubdirectory("linuxfiles-html-").FullName;
			try
			{
				var path = Path.Combine(root, "script test.html");
				await File.WriteAllTextAsync(path, "<html><body style='margin:0;background:rgb(0,255,0)'><script>document.body.style.background='rgb(255,0,0)';</script></body></html>");
				var png = await HtmlSnapshotRenderer.RenderAsync(path, 320, scale, CancellationToken.None);
				Assert.IsNotNull(png, "Snapshot helper failed.");
				using var bitmap = SKBitmap.Decode(png);
				Assert.IsNotNull(bitmap);
				Assert.AreEqual(320 * scale, bitmap.Width);
				var color = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
				Assert.AreEqual((byte)0, color.Red);
				Assert.AreEqual((byte)255, color.Green);
			}
			finally { Directory.Delete(root, true); }
		}

		[TestMethod]
		public async Task BlocksNetworkImagesAndStylesIncludingLocalCssImports()
		{
			RequireIntegration();
			var root = Directory.CreateTempSubdirectory("linuxfiles-html-network-").FullName;
			using var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			using var stop = new CancellationTokenSource();
			var accepted = 0;
			var accepting = Task.Run(async () =>
			{
				try
				{
					while (!stop.IsCancellationRequested)
					{
						using var client = await listener.AcceptTcpClientAsync(stop.Token);
						Interlocked.Increment(ref accepted);
					}
				}
				catch (OperationCanceledException) { }
			});
			try
			{
				var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
				await File.WriteAllTextAsync(Path.Combine(root, "local.css"), $"@import url('{url}/nested.css'); body {{background:rgb(0,255,0)}}");
				var path = Path.Combine(root, "network.html");
				await File.WriteAllTextAsync(path, $"<link rel='stylesheet' href='local.css'><link rel='stylesheet' href='{url}/remote.css'>" +
					$"<style>@import url('{url}/import.css');</style><img src='{url}/x.png'>" +
					$"<link rel='preconnect' href='{url}'><iframe src='{url}/frame'></iframe>" +
					$"<meta http-equiv='refresh' content='0;url={url}/navigate'>");
				var png = await HtmlSnapshotRenderer.RenderAsync(path, 320, 1, CancellationToken.None);
				Assert.IsNotNull(png, "Snapshot helper failed.");
				using var bitmap = SKBitmap.Decode(png);
				Assert.IsNotNull(bitmap);
				Assert.AreEqual(SKColors.Lime, bitmap.GetPixel(160, 50), "Relative local stylesheet must still load.");
				await Task.Delay(150);
				Assert.AreEqual(0, Volatile.Read(ref accepted), "Local HTML made a network connection.");
			}
			finally
			{
				stop.Cancel();
				await accepting;
				Directory.Delete(root, true);
			}
		}

		[TestMethod]
		public async Task CancellationTerminatesTheHelperProcess()
		{
			RequireIntegration();
			var root = Directory.CreateTempSubdirectory("linuxfiles-html-cancel-").FullName;
			try
			{
				var path = Path.Combine(root, "cancel.html");
				await File.WriteAllTextAsync(path, "<p>Cancel</p>");
				using var cancel = new CancellationTokenSource();
				Process? helper = null;
				try
				{
					var start = HtmlSnapshotRenderer.CreateStartInfo(Environment.ProcessPath!, Environment.GetCommandLineArgs()[0], path, 320, 1);
					await Assert.ThrowsAsync<OperationCanceledException>(() => HtmlSnapshotRenderer.RunProcessAsync(start, cancel.Token, pid =>
					{
						helper = Process.GetProcessById(pid);
						cancel.Cancel();
					}));
					Assert.IsNotNull(helper);
					Assert.IsTrue(helper.HasExited, "Renderer must reap its helper before completing cancellation.");
				}
				finally { helper?.Dispose(); }
			}
			finally { Directory.Delete(root, true); }
		}

		[TestMethod]
		public async Task CropsFullDocumentToMaximumPixelHeight()
		{
			RequireIntegration();
			var root = Directory.CreateTempSubdirectory("linuxfiles-html-crop-").FullName;
			try
			{
				var path = Path.Combine(root, "tall.html");
				await File.WriteAllTextAsync(path, "<body style='margin:0'><div style='height:1200px;width:1000px;background:linear-gradient(lime,blue)'></div>");
				var start = HtmlSnapshotRenderer.CreateStartInfo(Environment.ProcessPath!, Environment.GetCommandLineArgs()[0], path, 320, 1);
				start.ArgumentList[^1] = "700";
				var png = await HtmlSnapshotRenderer.RunProcessAsync(start, CancellationToken.None);
				Assert.IsNotNull(png);
				using var bitmap = SKBitmap.Decode(png);
				Assert.IsNotNull(bitmap);
				Assert.AreEqual(320, bitmap.Width);
				Assert.AreEqual(700, bitmap.Height);
			}
			finally { Directory.Delete(root, true); }
		}

		[TestMethod]
		public async Task RefusesToRenderWithoutTheWebKitSandboxTools()
		{
			var root = Directory.CreateTempSubdirectory("linuxfiles-html-nosandbox-").FullName;
			try
			{
				var path = Path.Combine(root, "page.html");
				await File.WriteAllTextAsync(path, "<p>Sandbox</p>");
				// The check runs before GTK is initialized, so no display is needed.
				var start = HtmlSnapshotRenderer.CreateStartInfo(Environment.ProcessPath!, Environment.GetCommandLineArgs()[0], path, 320, 1);
				start.Environment["PATH"] = root;
				Assert.IsNull(await HtmlSnapshotRenderer.RunProcessAsync(start, CancellationToken.None));
			}
			finally { Directory.Delete(root, true); }
		}

		[TestMethod]
		public async Task RejectsOversizedDocumentsInHelper()
		{
			var root = Directory.CreateTempSubdirectory("linuxfiles-html-limit-").FullName;
			try
			{
				var path = Path.Combine(root, "large.html");
				await using (var file = File.Create(path)) file.SetLength(HtmlSnapshotHelper.MaxDocumentBytes + 1L);
				// Rejection happens before GTK is initialized, so this also tests self-launch without a display.
				var start = HtmlSnapshotRenderer.CreateStartInfo(Environment.ProcessPath!, Environment.GetCommandLineArgs()[0], path, 320, 1);
				Assert.IsNull(await HtmlSnapshotRenderer.RunProcessAsync(start, CancellationToken.None));
			}
			finally { Directory.Delete(root, true); }
		}

		private static void RequireIntegration()
		{
			if (!HtmlSnapshotRenderer.IsAvailable ||
				(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))))
				Assert.Inconclusive("HTML snapshot integration requires WebKitGTK 4.1, GTK3 and a private usable display.");
		}
	}
}
