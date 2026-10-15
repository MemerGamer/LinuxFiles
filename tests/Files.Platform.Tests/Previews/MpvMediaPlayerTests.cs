// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Previews
{
	[TestClass]
	public sealed class MpvMediaPlayerTests
	{
		[TestMethod]
		[DataRow(640, 360, 640, 480, 480, 360)]
		[DataRow(5000, 5000, 3840, 2160, 1920, 1080)]
		[DataRow(1920, 1080, 1080, 1920, 608, 1080)]
		[DataRow(0, -1, 64, 48, 1, 1)]
		[DataRow(640, 360, 0, 0, 640, 360)]
		[DataRow(int.MaxValue, int.MaxValue, int.MaxValue, 1, 1920, 1)]
		public void FitsAspectRatioWithinBoundedTarget(int width, int height, int videoWidth, int videoHeight, int expectedWidth, int expectedHeight)
		{
			Assert.AreEqual((expectedWidth, expectedHeight), MpvMediaPlayer.FitSize(width, height, videoWidth, videoHeight));
		}

		[TestMethod]
		public void OptionsDisableExternalInputsAndPlaylistDemuxers()
		{
			var options = MpvMediaPlayer.Options.ToArray().ToDictionary(p => p.Name, p => p.Value);
			foreach (var name in new[] { "config", "load-scripts", "ytdl", "access-references", "load-unsafe-playlists", "autoload-files", "sub-auto", "audio-file-auto", "cover-art-auto", "autocreate-playlist", "prefetch-playlist", "ordered-chapters" })
				Assert.AreEqual("no", options[name], name);
			Assert.AreEqual("lavf", options["demuxer"]);
			StringAssert.StartsWith(options["demuxer-lavf-o"], "protocol_whitelist=[file],format_whitelist=[");
			foreach (var format in new[] { "hls", "dash", "concat", "sdp" })
				Assert.IsFalse(options["demuxer-lavf-o"].Contains(format, StringComparison.Ordinal));
			Assert.AreEqual("protocol_whitelist=[file]", options["stream-lavf-o"]);
			Assert.AreEqual("libmpv", options["vo"]);
			Assert.AreEqual("no", options["hwdec"]);
			Assert.AreEqual("yes", options["pause"]);
		}

		[TestMethod]
		public async Task RejectsRelativePathsWithoutLoadingNativeCode()
		{
			using var player = new MpvMediaPlayer();
			Assert.ThrowsExactly<ArgumentException>(() => player.Open("relative.mp4"));
			player.Dispose();
			player.Dispose();
			await player.Disposal.WaitAsync(TimeSpan.FromSeconds(10));
		}

		[TestMethod]
		public async Task RendersOpaqueOwnedFramesAndResizesWhilePaused()
		{
			RequireIntegration();
			var root = Directory.CreateTempSubdirectory("linuxfiles-mpv-").FullName;
			using var player = new MpvMediaPlayer();
			try
			{
				var path = await CreateVideo(root);
				player.SetTargetSize(128, 96);
				var first = new TaskCompletionSource<MpvFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
				var resized = new TaskCompletionSource<MpvFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
				player.FrameRendered += frame =>
				{
					if (frame.Width == 128 && frame.Height == 96) first.TrySetResult(frame);
					if (frame.Width == 80 && frame.Height == 60) resized.TrySetResult(frame);
				};
				player.Open(path);
				var frame = await first.Task.WaitAsync(TimeSpan.FromSeconds(15));
				Assert.IsNull(player.Error);
				Assert.IsTrue(player.IsPaused);
				Assert.IsTrue(player.HasVideo);
				Assert.AreEqual(64, player.VideoWidth);
				Assert.AreEqual(48, player.VideoHeight);
				Assert.AreEqual(frame.Width * 4, frame.Stride);
				AssertOpaque(frame);
				Assert.IsTrue(frame.Pixels.ToArray().Where((_, i) => i % 4 != 3).Distinct().Count() > 5, "Expected a decoded test pattern.");
				var retained = frame.Pixels.ToArray();
				player.SetTargetSize(80, 60);
				AssertOpaque(await resized.Task.WaitAsync(TimeSpan.FromSeconds(10)));
				player.Play();
				await WaitUntil(() => player.IsEnded, player);
				Assert.IsTrue(player.Duration.TotalSeconds > 0);
				player.Seek(TimeSpan.Zero);
				player.Pause();
				player.Dispose();
				await player.Disposal.WaitAsync(TimeSpan.FromSeconds(10));
				CollectionAssert.AreEqual(retained, frame.Pixels.ToArray());
			}
			finally
			{
				player.Dispose();
				await player.Disposal.WaitAsync(TimeSpan.FromSeconds(10));
				Directory.Delete(root, true);
			}
		}

		[TestMethod]
		public async Task DisposeDuringPlaybackAndDoubleDisposeComplete()
		{
			RequireIntegration();
			var root = Directory.CreateTempSubdirectory("linuxfiles-mpv-").FullName;
			using var player = new MpvMediaPlayer();
			try
			{
				var path = await CreateVideo(root);
				player.Open(path);
				player.Play(); // Also exercises a play request made before FILE_LOADED.
				await WaitUntil(() => player.Position > TimeSpan.Zero, player);
				var stopwatch = Stopwatch.StartNew();
				player.Dispose();
				Assert.IsLessThan(TimeSpan.FromSeconds(1), stopwatch.Elapsed);
				await Task.WhenAll(Task.Run(player.Dispose), Task.Run(player.Dispose));
				await player.Disposal.WaitAsync(TimeSpan.FromSeconds(10));
				Assert.ThrowsExactly<ObjectDisposedException>(player.Play);
			}
			finally
			{
				player.Dispose();
				await player.Disposal.WaitAsync(TimeSpan.FromSeconds(10));
				Directory.Delete(root, true);
			}
		}

		[TestMethod]
		public async Task DisposeFromFrameCallbackCompletes()
		{
			RequireIntegration();
			var root = Directory.CreateTempSubdirectory("linuxfiles-mpv-").FullName;
			using var player = new MpvMediaPlayer();
			try
			{
				var path = await CreateVideo(root);
				var rendered = new TaskCompletionSource<MpvFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
				player.FrameRendered += frame =>
				{
					player.Dispose();
					rendered.TrySetResult(frame);
				};
				player.Open(path);
				var frame = await rendered.Task.WaitAsync(TimeSpan.FromSeconds(15));
				await player.Disposal.WaitAsync(TimeSpan.FromSeconds(10));
				AssertOpaque(frame);
			}
			finally
			{
				player.Dispose();
				await player.Disposal.WaitAsync(TimeSpan.FromSeconds(10));
				Directory.Delete(root, true);
			}
		}

		[TestMethod]
		[DataRow("#EXTM3U\nhttp://127.0.0.1:9/should-not-open.mp4\n")]
		[DataRow("[playlist]\nNumberOfEntries=1\nFile1=http://127.0.0.1:9/should-not-open.mp4\n")]
		[DataRow("#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXTINF:1,\nhttp://127.0.0.1:9/should-not-open.ts\n#EXT-X-ENDLIST\n")]
		[DataRow("#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXTINF:1,\ntest video.mp4\n#EXT-X-ENDLIST\n")]
		[DataRow("ffconcat version 1.0\nfile 'test video.mp4'\n")]
		public async Task DisguisedPlaylistFailsWithoutPublishingFrames(string content)
		{
			RequireIntegration();
			var root = Directory.CreateTempSubdirectory("linuxfiles-mpv-").FullName;
			using var player = new MpvMediaPlayer();
			try
			{
				if (content.Contains("test video.mp4", StringComparison.Ordinal)) await CreateVideo(root);
				var path = Path.Combine(root, "playlist.mp4");
				await File.WriteAllTextAsync(path, content);
				var frames = 0;
				player.FrameRendered += _ => Interlocked.Increment(ref frames);
				player.Open(path);
				player.Play();
				await WaitUntil(() => player.Error is not null, player, allowFailure: true);
				Assert.IsNotNull(player.Error);
				Assert.AreEqual(0, Volatile.Read(ref frames));
				Assert.IsTrue(player.IsPaused);
			}
			finally
			{
				player.Dispose();
				await player.Disposal.WaitAsync(TimeSpan.FromSeconds(10));
				Directory.Delete(root, true);
			}
		}

		private static void RequireIntegration()
		{
			if (!MpvMediaPlayer.IsAvailable || !File.Exists("/usr/bin/ffmpeg"))
				Assert.Inconclusive("Media integration requires libmpv client API 2 and ffmpeg.");
		}

		private static async Task<string> CreateVideo(string root)
		{
			var path = Path.Combine(root, "test video.mp4");
			using var process = Process.Start(new ProcessStartInfo("/usr/bin/ffmpeg",
				["-nostdin", "-loglevel", "error", "-y", "-filter_threads", "1", "-f", "lavfi", "-i", "testsrc=size=64x48:rate=10:duration=1", "-c:v", "mpeg4", "-threads", "1", "-pix_fmt", "yuv420p", path]) { UseShellExecute = false })!;
			try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
			finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
			Assert.AreEqual(0, process.ExitCode, "ffmpeg test video generation failed.");
			return path;
		}

		private static void AssertOpaque(MpvFrame frame)
		{
			Assert.AreEqual(frame.Stride * frame.Height, frame.Pixels.Length);
			for (var i = 3; i < frame.Pixels.Length; i += 4) Assert.AreEqual((byte)255, frame.Pixels.Span[i]);
		}

		private static async Task WaitUntil(Func<bool> condition, MpvMediaPlayer player, bool allowFailure = false)
		{
			var deadline = Stopwatch.StartNew();
			while (!condition())
			{
				if (!allowFailure) Assert.IsNull(player.Error);
				if (deadline.Elapsed > TimeSpan.FromSeconds(15)) Assert.Fail("Timed out waiting for media state: " + player.Error);
				await Task.Delay(20);
			}
		}
	}
}
