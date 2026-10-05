// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Thumbnails;
using Files.Platform.Linux.Thumbnails;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Thumbnails
{
	[TestClass]
	public sealed class ThumbnailTests
	{
		private string _root = string.Empty;
		private string _cacheHome = string.Empty;
		private string _files = string.Empty;

		[TestInitialize]
		public void Setup()
		{
			_root = Path.Combine(Path.GetTempPath(), "files-thumb-" + Guid.NewGuid().ToString("N"));
			_cacheHome = Path.Combine(_root, "cache");
			_files = Path.Combine(_root, "files");
			Directory.CreateDirectory(_files);
		}

		[TestCleanup]
		public void Cleanup()
		{
			Directory.Delete(_root, true);
		}

		private LinuxThumbnailService CreateService(Action<LinuxThumbnailOptions>? configure = null)
		{
			var options = new LinuxThumbnailOptions
			{
				CacheHome = _cacheHome,
				ThumbnailerDirectories = [],
				ThumbnailerTempRoot = Path.Combine(_root, "tmp-root"),
			};
			configure?.Invoke(options);
			return new LinuxThumbnailService(options);
		}

		private string CreateImage(string name, int width, int height)
		{
			var path = Path.Combine(_files, name);
			File.WriteAllBytes(path, TestImages.CreatePng(width, height));
			File.SetLastWriteTimeUtc(path, new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
			return path;
		}

		private string CachePath(string path, string bucket) =>
			Path.Combine(_cacheHome, "thumbnails", bucket, XdgThumbnailNaming.GetHash(XdgThumbnailNaming.ToFileUri(path)) + ".png");

		[TestMethod]
		public void Naming_MatchesSpecKnownVector()
		{
			// Example from the XDG Thumbnail Managing Standard.
			var uri = "file:///home/jens/photos/me.png";

			Assert.AreEqual(uri, XdgThumbnailNaming.ToFileUri("/home/jens/photos/me.png"));
			Assert.AreEqual("c6ee772d9e49320e97ec29a7eb5b1697", XdgThumbnailNaming.GetHash(uri));
		}

		[TestMethod]
		public void Naming_PercentEncodesUri()
		{
			Assert.AreEqual("file:///a%20b/%C3%A9%23%25.png", XdgThumbnailNaming.ToFileUri("/a b/é#%.png"));
			Assert.AreEqual("file:///x/a:b@c,d.png", XdgThumbnailNaming.ToFileUri("/x/a:b@c,d.png"));
		}

		[TestMethod]
		[DataRow(1u, "normal")]
		[DataRow(128u, "normal")]
		[DataRow(129u, "large")]
		[DataRow(256u, "large")]
		[DataRow(300u, "x-large")]
		[DataRow(512u, "x-large")]
		[DataRow(513u, "xx-large")]
		[DataRow(4000u, "xx-large")]
		public void Naming_PicksBucket(uint size, string expected)
		{
			Assert.AreEqual(expected, XdgThumbnailNaming.GetBucketName(size));
		}

		[TestMethod]
		public void PngText_RoundTripsAndStaysDecodable()
		{
			var png = TestImages.CreatePng(4, 3);

			var withText = PngTextChunks.Insert(png, [new("Thumb::URI", "file:///x"), new("Thumb::MTime", "42")]);
			var text = PngTextChunks.Read(withText);

			Assert.AreEqual("file:///x", text["Thumb::URI"]);
			Assert.AreEqual("42", text["Thumb::MTime"]);
			using var bitmap = SKBitmap.Decode(withText);
			Assert.AreEqual(4, bitmap.Width);
			Assert.AreEqual(3, bitmap.Height);
		}

		[TestMethod]
		public async Task Generate_WritesSpecCompliantCacheEntry()
		{
			var path = CreateImage("big.png", 400, 200);
			using var service = CreateService();

			var bytes = await service.GetThumbnailAsync(path, 64);

			Assert.IsNotNull(bytes);
			using var bitmap = SKBitmap.Decode(bytes);
			Assert.AreEqual(128, bitmap.Width);
			Assert.AreEqual(64, bitmap.Height);

			var cached = CachePath(path, "normal");
			Assert.IsTrue(File.Exists(cached));
			var text = PngTextChunks.Read(File.ReadAllBytes(cached));
			Assert.AreEqual(XdgThumbnailNaming.ToFileUri(path), text["Thumb::URI"]);
			Assert.AreEqual(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero).ToUnixTimeSeconds().ToString(), text["Thumb::MTime"]);
			Assert.AreEqual(new FileInfo(path).Length.ToString(), text["Thumb::Size"]);
			if (!OperatingSystem.IsWindows())
				Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(cached));
			Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(cached)!, "*.tmp").Length);
		}

		[TestMethod]
		public async Task Generate_DoesNotUpscaleSmallImages()
		{
			var path = CreateImage("small.png", 20, 10);
			using var service = CreateService();

			var bytes = await service.GetThumbnailAsync(path, 256);

			using var bitmap = SKBitmap.Decode(bytes);
			Assert.AreEqual(20, bitmap.Width);
			Assert.AreEqual(10, bitmap.Height);
			Assert.IsTrue(File.Exists(CachePath(path, "large")));
		}

		[TestMethod]
		public async Task Cache_HitAndMtimeInvalidation()
		{
			var path = CreateImage("a.png", 300, 300);
			using var service = CreateService();

			Assert.IsNull(await service.GetThumbnailAsync(path, 128, ThumbnailOptions.ReturnOnlyIfCached));
			Assert.IsNotNull(await service.GetThumbnailAsync(path, 128));
			Assert.IsNotNull(await service.GetThumbnailAsync(path, 128, ThumbnailOptions.ReturnOnlyIfCached));

			File.SetLastWriteTimeUtc(path, new DateTime(2025, 5, 5, 5, 5, 5, DateTimeKind.Utc));

			Assert.IsNull(await service.GetThumbnailAsync(path, 128, ThumbnailOptions.ReturnOnlyIfCached));
			Assert.IsNotNull(await service.GetThumbnailAsync(path, 128));
			Assert.IsNotNull(await service.GetThumbnailAsync(path, 128, ThumbnailOptions.ReturnOnlyIfCached));
		}

		[TestMethod]
		public async Task Cache_IsReadFromDiskNotRegenerated()
		{
			var path = CreateImage("a.png", 300, 300);
			using var service = CreateService();
			await service.GetThumbnailAsync(path, 128);

			var cached = CachePath(path, "normal");
			var marker = PngTextChunks.Insert(TestImages.CreatePng(1, 1), [new("Thumb::URI", XdgThumbnailNaming.ToFileUri(path)), new("Thumb::MTime", new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero).ToUnixTimeSeconds().ToString())]);
			File.WriteAllBytes(cached, marker);

			var bytes = await service.GetThumbnailAsync(path, 128);
			using var bitmap = SKBitmap.Decode(bytes);
			Assert.AreEqual(1, bitmap.Width);

			var forced = await service.GetThumbnailAsync(path, 128, ThumbnailOptions.ForceRegenerate);
			using var regenerated = SKBitmap.Decode(forced);
			Assert.AreEqual(128, regenerated.Width);
		}

		[TestMethod]
		public async Task Cache_RejectsEntryWithWrongUri()
		{
			var path = CreateImage("a.png", 300, 300);
			using var service = CreateService();
			await service.GetThumbnailAsync(path, 128);
			var cached = CachePath(path, "normal");
			File.WriteAllBytes(cached, PngTextChunks.Insert(TestImages.CreatePng(1, 1), [new("Thumb::URI", "file:///other"), new("Thumb::MTime", "1")]));

			Assert.IsNull(await service.GetThumbnailAsync(path, 128, ThumbnailOptions.ReturnOnlyIfCached));
		}

		[TestMethod]
		public async Task Failure_IsRecordedAndHonoredUntilMtimeChanges()
		{
			var path = Path.Combine(_files, "broken.png");
			File.WriteAllText(path, "not an image");
			var mtime = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
			File.SetLastWriteTimeUtc(path, mtime);
			using var service = CreateService();

			Assert.IsNull(await service.GetThumbnailAsync(path, 128));

			var hash = XdgThumbnailNaming.GetHash(XdgThumbnailNaming.ToFileUri(path));
			var failFile = Path.Combine(_cacheHome, "thumbnails", "fail", "files-1.0", hash + ".png");
			Assert.IsTrue(File.Exists(failFile));
			Assert.IsFalse(File.Exists(CachePath(path, "normal")));

			// Same mtime: the failure is remembered even though the content is now decodable.
			File.WriteAllBytes(path, TestImages.CreatePng(10, 10));
			File.SetLastWriteTimeUtc(path, mtime);
			Assert.IsNull(await service.GetThumbnailAsync(path, 128));

			File.SetLastWriteTimeUtc(path, mtime.AddMinutes(1));
			Assert.IsNotNull(await service.GetThumbnailAsync(path, 128));
		}

		[TestMethod]
		public async Task UnsupportedFileAndMissingFile_ReturnNull()
		{
			var path = Path.Combine(_files, "notes.txt");
			File.WriteAllText(path, "hello");
			using var service = CreateService();

			Assert.IsNull(await service.GetThumbnailAsync(path, 128));
			Assert.IsNull(await service.GetThumbnailAsync(Path.Combine(_files, "missing.png"), 128));
			Assert.IsNull(await service.GetThumbnailAsync(_files, 128));
			Assert.IsNull(await service.GetThumbnailAsync("relative.png", 128));
		}

		[TestMethod]
		public async Task Cancellation_Throws()
		{
			var path = CreateImage("a.png", 50, 50);
			using var service = CreateService();
			using var cts = new CancellationTokenSource();
			cts.Cancel();

			await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetThumbnailAsync(path, 128, ThumbnailOptions.None, cts.Token));
		}

		[TestMethod]
		public void ThumbnailerEntry_ParsesAndExpandsExec()
		{
			var entry = ThumbnailerEntry.Parse("[Thumbnailer Entry]\nTryExec=foo\nExec=foo -s %s \"%i\" --uri=%u -o %o 100%%\nMimeType=application/pdf;image/x-foo;\n");

			Assert.IsNotNull(entry);
			CollectionAssert.AreEqual(new[] { "application/pdf", "image/x-foo" }, new List<string>(entry.MimeTypes));
			var command = entry.BuildCommand("/a b/in.pdf", "file:///a%20b/in.pdf", "/tmp/out.png", 256)!.Value;
			Assert.AreEqual("foo", command.FileName);
			CollectionAssert.AreEqual(new[] { "-s", "256", "/a b/in.pdf", "--uri=file:///a%20b/in.pdf", "-o", "/tmp/out.png", "100%" }, new List<string>(command.Arguments));
			Assert.IsNull(ThumbnailerEntry.Parse("[Thumbnailer Entry]\nExec=foo\n"));
		}

		[TestMethod]
		public async Task ExternalThumbnailer_IsInvokedForMappedMimeType()
		{
			var dir = Path.Combine(_root, "thumbnailers");
			Directory.CreateDirectory(dir);
			File.WriteAllText(Path.Combine(dir, "fake.thumbnailer"), "[Thumbnailer Entry]\nExec=fake-thumb -s %s %i %o\nMimeType=application/x-fake;\n");
			var path = Path.Combine(_files, "doc.fake");
			File.WriteAllText(path, "data");
			var runner = new FakeRunner();
			using var service = CreateService(o =>
			{
				o.ThumbnailerDirectories = [dir];
				o.MimeTypeResolver = p => p.EndsWith(".fake", StringComparison.Ordinal) ? "application/x-fake" : null;
				o.ProcessRunner = runner;
				o.SandboxExternalThumbnailers = false;
				o.AllowUnsandboxedExternalThumbnailers = true;
			});

			var bytes = await service.GetThumbnailAsync(path, 256);

			Assert.IsNotNull(bytes);
			Assert.AreEqual("fake-thumb", runner.FileName);
			CollectionAssert.AreEqual(new[] { "-s", "256", path }, new List<string>(runner.Arguments).GetRange(0, 3));
			Assert.IsTrue(File.Exists(CachePath(path, "large")));
			Assert.AreEqual(XdgThumbnailNaming.ToFileUri(path), PngTextChunks.Read(bytes)["Thumb::URI"]);
		}

		[TestMethod]
		public async Task Generate_RefusesImagesOverPixelLimitAndRecordsFailure()
		{
			var path = CreateImage("huge.png", 20, 20);
			using var service = CreateService(o => o.MaxImagePixels = 100);

			Assert.IsNull(await service.GetThumbnailAsync(path, 128));

			var hash = XdgThumbnailNaming.GetHash(XdgThumbnailNaming.ToFileUri(path));
			Assert.IsTrue(File.Exists(Path.Combine(_cacheHome, "thumbnails", "fail", "files-1.0", hash + ".png")));
			Assert.IsFalse(File.Exists(CachePath(path, "normal")));
		}

		[TestMethod]
		public async Task Generate_RefusesForgedHugeHeaderWithoutThrowing()
		{
			var png = TestImages.CreatePng(4, 4);
			// Forge IHDR width/height (bytes 16..23) to 50000x50000.
			var forged = new byte[] { 0, 0, 0xC3, 0x50, 0, 0, 0xC3, 0x50 };
			Array.Copy(forged, 0, png, 16, 8);
			var path = Path.Combine(_files, "bomb.png");
			File.WriteAllBytes(path, png);
			using var service = CreateService();

			Assert.IsNull(await service.GetThumbnailAsync(path, 128));
		}

		[TestMethod]
		public async Task Generate_RefusesSourceFilesOverSizeLimit()
		{
			var path = CreateImage("a.png", 50, 50);
			using var service = CreateService(o => o.MaxSourceFileBytes = 10);

			Assert.IsNull(await service.GetThumbnailAsync(path, 128));
		}

		[TestMethod]
		public async Task Cache_IgnoresEntriesOverSizeLimit()
		{
			var path = CreateImage("a.png", 300, 300);
			using (var writer = CreateService())
				Assert.IsNotNull(await writer.GetThumbnailAsync(path, 128));

			using var service = CreateService(o => o.MaxCacheEntryBytes = 10);

			Assert.IsNull(await service.GetThumbnailAsync(path, 128, ThumbnailOptions.ReturnOnlyIfCached));
		}

		[TestMethod]
		public void BuildCommand_DashPrefixedRelativeNameBecomesAbsolutePath()
		{
			var entry = ThumbnailerEntry.Parse("[Thumbnailer Entry]\nExec=thumb %i %u %o\nMimeType=a/b;\n")!;

			var command = entry.BuildCommand("-rf", "file:///x/-rf", "/out/o.png", 128)!.Value;

			Assert.IsTrue(command.Arguments[0].StartsWith('/'));
			Assert.IsTrue(command.Arguments[0].EndsWith("/-rf", StringComparison.Ordinal));
			Assert.IsNull(entry.BuildCommand("/x/a", "http://x/a", "/out/o.png", 128));
		}

		[TestMethod]
		[DataRow("./thumb %i")]
		[DataRow("bin/thumb %i")]
		[DataRow("\"\" %i")]
		public void BuildCommand_RejectsEmptyOrRelativePathPrograms(string exec)
		{
			var entry = ThumbnailerEntry.Parse("[Thumbnailer Entry]\nExec=" + exec + "\nMimeType=a/b;\n")!;

			Assert.IsNull(entry.BuildCommand("/x/a", "file:///x/a", "/out/o.png", 128));
		}

		private static (string, IReadOnlyList<string>) FakeWrap(string[]? symlinks = null)
		{
			var links = new HashSet<string>(symlinks ?? ["/bin", "/sbin", "/lib", "/lib64"]);
			return BubblewrapSandbox.Wrap("/usr/bin/thumb", ["-s", "128", "/x/a"], "/cache/normal", "/x/a",
				p => links.Contains(p) ? "usr" + p : null,
				p => p is "/lib32" or "/etc/fonts" or "/etc/ld.so.cache");
		}

		[TestMethod]
		public void Bubblewrap_BuildsMinimalSandboxArguments()
		{
			var (fileName, args) = FakeWrap();
			var list = new List<string>(args);

			Assert.AreEqual("bwrap", fileName);
			CollectionAssert.AreEqual(new[] { "--ro-bind", "/usr", "/usr" }, list.GetRange(list.IndexOf("--ro-bind"), 3));
			CollectionAssert.IsSubsetOf(new[] { "--unshare-all", "--die-with-parent", "--new-session", "--clearenv", "--proc", "--dev", "--tmpfs" }, list);
			CollectionAssert.AreEqual(new[] { "--symlink", "usr/bin", "/bin" }, list.GetRange(list.IndexOf("--symlink"), 3));
			Assert.IsTrue(ContainsSequence(list, "--ro-bind", "/lib32", "/lib32"));
			Assert.IsTrue(ContainsSequence(list, "--ro-bind", "/etc/fonts", "/etc/fonts"));
			Assert.IsTrue(ContainsSequence(list, "--ro-bind", "/x/a", "/x/a"));
			Assert.IsTrue(ContainsSequence(list, "--bind", "/cache/normal", "/cache/normal"));
			Assert.IsTrue(ContainsSequence(list, "--setenv", "PATH", "/usr/bin:/bin"));
			Assert.IsTrue(ContainsSequence(list, "--chdir", "/", "--"));
			CollectionAssert.AreEqual(new[] { "/usr/bin/thumb", "-s", "128", "/x/a" }, list.GetRange(list.Count - 4, 4));
		}

		[TestMethod]
		public void Bubblewrap_NeverExposesRootHomeOrRun()
		{
			var (_, args) = FakeWrap();
			var list = new List<string>(args);

			Assert.IsFalse(ContainsSequence(list, "--ro-bind", "/", "/"));
			Assert.IsFalse(ContainsSequence(list, "--bind", "/", "/"));
			foreach (var arg in list)
			{
				Assert.IsFalse(arg == "/home" || arg.StartsWith("/home/", StringComparison.Ordinal), arg);
				Assert.IsFalse(arg == "/run" || arg.StartsWith("/run/", StringComparison.Ordinal) || arg.StartsWith("/var/run", StringComparison.Ordinal), arg);
			}
		}

		private static bool ContainsSequence(List<string> list, params string[] seq)
		{
			for (var i = 0; i + seq.Length <= list.Count; i++)
			{
				if (list.GetRange(i, seq.Length).SequenceEqual(seq))
					return true;
			}

			return false;
		}

		[TestMethod]
		public async Task Bubblewrap_Integration_RunDirIsNotVisible()
		{
			if (!BubblewrapSandbox.IsAvailable())
				return;

			var input = Path.Combine(_files, "in.txt");
			File.WriteAllText(input, "x");
			var runner = new ThumbnailerProcessRunner();

			var (control, controlArgs) = BubblewrapSandbox.Wrap("ls", ["/usr"], _files, input);
			var (probe, probeArgs) = BubblewrapSandbox.Wrap("ls", ["/run/user"], _files, input);

			Assert.IsTrue(await runner.RunAsync(control, controlArgs, CancellationToken.None), "sandbox itself should work");
			Assert.IsFalse(await runner.RunAsync(probe, probeArgs, CancellationToken.None));
		}

		private async Task<FakeRunner> RunExternalAsync(Action<LinuxThumbnailOptions> configure, Action<string>? writer = null, Action<byte[]?>? result = null)
		{
			var dir = Path.Combine(_root, "thumbnailers");
			Directory.CreateDirectory(dir);
			File.WriteAllText(Path.Combine(dir, "fake.thumbnailer"), "[Thumbnailer Entry]\nExec=fake-thumb %i %o\nMimeType=application/x-fake;\n");
			var path = Path.Combine(_files, "doc.fake");
			File.WriteAllText(path, "data");
			var runner = new FakeRunner();
			if (writer is not null)
				runner.OutputWriter = writer;
			using var service = CreateService(o =>
			{
				o.ThumbnailerDirectories = [dir];
				o.MimeTypeResolver = _ => "application/x-fake";
				o.ProcessRunner = runner;
				o.ThumbnailerTempRoot = Path.Combine(_root, "tmp-root");
				configure(o);
			});

			var generated = await service.GetThumbnailAsync(path, 128);
			result?.Invoke(generated);
			return runner;
		}

		[TestMethod]
		public async Task ExternalThumbnailer_IsSandboxedWhenAvailable()
		{
			var runner = await RunExternalAsync(o => o.IsSandboxAvailable = () => true);

			Assert.AreEqual("bwrap", runner.FileName);
		}

		[TestMethod]
		public async Task ExternalThumbnailer_IsSkippedWithoutSandboxByDefault()
		{
			var runner = await RunExternalAsync(o => o.IsSandboxAvailable = () => false);

			Assert.IsNull(runner.FileName);
		}

		[TestMethod]
		public async Task ExternalThumbnailer_RunsUnsandboxedOnlyWhenExplicitlyAllowed()
		{
			var runner = await RunExternalAsync(o =>
			{
				o.IsSandboxAvailable = () => false;
				o.AllowUnsandboxedExternalThumbnailers = true;
			});

			Assert.AreEqual("fake-thumb", runner.FileName);
		}

		private static readonly Action<LinuxThumbnailOptions> Unsandboxed = o =>
		{
			o.IsSandboxAvailable = () => false;
			o.AllowUnsandboxedExternalThumbnailers = true;
		};

		[TestMethod]
		public async Task ExternalThumbnailer_OutputDirIsPrivateTempDirNotCacheAndIsCleanedUp()
		{
			var runner = await RunExternalAsync(o => o.IsSandboxAvailable = () => true, result: b => Assert.IsNotNull(b));

			Assert.AreEqual("bwrap", runner.FileName);
			var bound = runner.Arguments[runner.Arguments.ToList().IndexOf("--bind") + 1];
			Assert.AreEqual(runner.OutputDirectory, bound);
			Assert.IsFalse(bound.StartsWith(Path.Combine(_cacheHome, "thumbnails"), StringComparison.Ordinal));
			Assert.IsTrue(bound.StartsWith(Path.Combine(_root, "tmp-root"), StringComparison.Ordinal));
			Assert.IsFalse(Directory.Exists(bound));
		}

		[TestMethod]
		public async Task ExternalThumbnailer_RejectsOversizedDimensions()
		{
			await RunExternalAsync(Unsandboxed, p => File.WriteAllBytes(p, TestImages.CreatePng(300, 10)), b => Assert.IsNull(b));
		}

		[TestMethod]
		public async Task ExternalThumbnailer_RejectsNonPngOutput()
		{
			await RunExternalAsync(Unsandboxed, p => File.WriteAllText(p, "this is definitely not a png at all, just text padding"), b => Assert.IsNull(b));
		}

		[TestMethod]
		public async Task ExternalThumbnailer_RejectsSymlinkOutput()
		{
			var real = Path.Combine(_files, "real.png");
			File.WriteAllBytes(real, TestImages.CreatePng(8, 8));
			var runner = await RunExternalAsync(Unsandboxed, p => File.CreateSymbolicLink(p, real), b => Assert.IsNull(b));

			Assert.IsTrue(File.Exists(real), "link target must survive cleanup");
			Assert.IsFalse(Directory.Exists(runner.OutputDirectory));
		}

		[TestMethod]
		public async Task ExternalThumbnailer_AcceptsValidOutputAndCleansUp()
		{
			var runner = await RunExternalAsync(Unsandboxed, result: b => Assert.IsNotNull(b));

			Assert.IsFalse(Directory.Exists(runner.OutputDirectory));
		}

		[TestMethod]
		public async Task PdfFallbackUsesSandboxAndLiteralPathAndCleansOutput()
		{
			var path = Path.Combine(_files, "-sample $(touch injected); 'quoted'.pdf");
			File.WriteAllText(path, "%PDF-1.4");
			var runner = new FakeRunner { OutputWriter = p => File.WriteAllBytes(p + ".png", TestImages.CreatePng(8, 8)) };
			using var service = CreateService(o =>
			{
				o.MimeTypeResolver = _ => "application/pdf";
				o.IsSandboxAvailable = () => true;
				o.SandboxExternalThumbnailers = true;
				o.ProcessRunner = runner;
				o.ThumbnailerTempRoot = Path.Combine(_root, "tmp-root");
			});
			Assert.IsNotNull(await service.GetThumbnailAsync(path, 1024));
			Assert.AreEqual("bwrap", runner.FileName);
			CollectionAssert.Contains(runner.Arguments.ToList(), path);
			CollectionAssert.Contains(runner.Arguments.ToList(), "pdftoppm");
			Assert.IsFalse(Directory.Exists(runner.OutputDirectory));
		}

		[TestMethod]
		public async Task PdfFallbackRendersFirstPage()
		{
			if (!BubblewrapSandbox.IsAvailable() || !File.Exists("/usr/bin/pdftoppm"))
				Assert.Inconclusive("PDF integration requires bubblewrap and pdftoppm.");
			var path = Path.Combine(_files, "first page.pdf");
			using (var document = SKDocument.CreatePdf(path))
			{
				document.BeginPage(160, 200).Clear(SKColors.Red);
				document.EndPage();
				document.BeginPage(160, 200).Clear(SKColors.Green);
				document.EndPage();
				document.Close();
			}
			using var service = CreateService(o =>
			{
				o.MimeTypeResolver = _ => "application/pdf";
				o.SandboxExternalThumbnailers = true;
			});
			var bytes = await service.GetThumbnailAsync(path, 1024);
			Assert.IsNotNull(bytes);
			using var image = SKBitmap.Decode(bytes);
			Assert.AreEqual(1024, image.Height);
			Assert.AreEqual(SKColors.Red, image.GetPixel(image.Width / 2, image.Height / 2));
			Assert.IsEmpty(Directory.GetDirectories(Path.Combine(_root, "tmp-root")));
		}

		private sealed class FakeRunner : IThumbnailerProcessRunner
		{
			public string? FileName { get; private set; }

			public string? OutputDirectory { get; private set; }

			public Action<string> OutputWriter { get; set; } = p => File.WriteAllBytes(p, TestImages.CreatePng(8, 8));

			public IReadOnlyList<string> Arguments { get; private set; } = [];

			public Task<bool> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
			{
				FileName = fileName;
				Arguments = arguments;
				OutputDirectory = Path.GetDirectoryName(arguments[^1]);
				OutputWriter(arguments[^1]);
				return Task.FromResult(true);
			}
		}
	}
}
