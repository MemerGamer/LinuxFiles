// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Files.Platform.Abstractions.Archives;
using Files.Platform.Abstractions.FileOperations;
using Files.Platform.Linux.Archives;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Platform.Tests.Archives
{
	[TestClass]
	[SupportedOSPlatform("linux")]
	public sealed class ArchiveServiceTests
	{
		private readonly LinuxArchiveService service = new();
		private string root = null!;

		private string Work => Path.Combine(root, "work");

		private string Out => Path.Combine(root, "out");

		[TestInitialize]
		public void Setup()
		{
			root = Path.Combine(Path.GetTempPath(), "files-archives-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Work);
			Directory.CreateDirectory(Out);
		}

		[TestCleanup]
		public void Cleanup()
		{
			if (Directory.Exists(root))
				Directory.Delete(root, true);
		}

		private string MakeTree()
		{
			var dir = Path.Combine(Work, "Documents");
			Directory.CreateDirectory(Path.Combine(dir, "sub", "empty"));
			File.WriteAllText(Path.Combine(dir, "a.txt"), "alpha");
			File.WriteAllText(Path.Combine(dir, "sub", "b.txt"), new string('b', 50_000));
			File.WriteAllText(Path.Combine(dir, "@odd -name.txt"), "odd");
			return dir;
		}

		private static string ZipWith(string path, params (string Name, byte[] Data)[] entries)
		{
			using var stream = File.Create(path);
			using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
			foreach (var (name, data) in entries)
			{
				using var entryStream = zip.CreateEntry(name).Open();
				entryStream.Write(data);
			}

			return path;
		}

		private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

		private void AssertNothingEscaped()
		{
			Assert.IsFalse(File.Exists(Path.Combine(root, "evil.txt")));
			Assert.IsFalse(File.Exists(Path.Combine(Work, "evil.txt")));
			Assert.IsFalse(File.Exists(Path.Combine(Out, "evil.txt")));
			Assert.AreEqual(0, Directory.GetFileSystemEntries(Out, "*", SearchOption.AllDirectories).Length, "destination must stay empty, staging removed");
		}

		// ---- create and extract round trips ----

		[TestMethod]
		[DataRow(ArchiveFormat.Zip)]
		[DataRow(ArchiveFormat.Tar)]
		[DataRow(ArchiveFormat.TarGz)]
		[DataRow(ArchiveFormat.TarBz2)]
		public async Task CreateThenExtractRoundTrips(ArchiveFormat format)
		{
			var tree = MakeTree();
			var archive = Path.Combine(Work, "docs" + service.GetExtension(format));

			var created = await service.CreateAsync([tree], archive, new ArchiveCreateOptions { Format = format });
			Assert.IsTrue(created.Succeeded, created.Error);
			Assert.IsTrue(service.IsArchiveFileName(archive));
			Assert.AreEqual(0, Directory.GetFiles(Work, ".*.tmp").Length);

			var listing = await service.ListAsync(archive);
			CollectionAssert.IsSubsetOf(new[] { "Documents/a.txt", "Documents/sub/b.txt" }, listing.Entries.Select(e => e.Path.TrimEnd('/')).ToList());

			var extracted = await service.ExtractAsync(archive, Out);
			Assert.IsTrue(extracted.Succeeded, extracted.Error);
			Assert.AreEqual("alpha", File.ReadAllText(Path.Combine(Out, "Documents", "a.txt")));
			Assert.AreEqual(50_000, File.ReadAllText(Path.Combine(Out, "Documents", "sub", "b.txt")).Length);
			Assert.AreEqual("odd", File.ReadAllText(Path.Combine(Out, "Documents", "@odd -name.txt")));
			Assert.IsTrue(Directory.Exists(Path.Combine(Out, "Documents", "sub", "empty")));
			Assert.AreEqual(1, Directory.GetFileSystemEntries(Out).Length, "no staging folder left behind");
		}

		[TestMethod]
		public async Task CompressionLevelsAffectSize()
		{
			var tree = MakeTree();
			var stored = Path.Combine(Work, "stored.zip");
			var best = Path.Combine(Work, "best.zip");
			await service.CreateAsync([tree], stored, new ArchiveCreateOptions { Level = ArchiveCompressionLevel.None });
			await service.CreateAsync([tree], best, new ArchiveCreateOptions { Level = ArchiveCompressionLevel.Ultra });
			Assert.IsTrue(new FileInfo(stored).Length > new FileInfo(best).Length);
		}

		[TestMethod]
		public async Task CreateRefusesToOverwriteExistingArchive()
		{
			var tree = MakeTree();
			var archive = Path.Combine(Work, "x.zip");
			File.WriteAllText(archive, "keep");
			var result = await service.CreateAsync([tree], archive);
			Assert.IsFalse(result.Succeeded);
			Assert.AreEqual("keep", File.ReadAllText(archive));
		}

		[TestMethod]
		public async Task CreateSkipsSymlinksAndExcludesTheArchiveItself()
		{
			var tree = MakeTree();
			File.CreateSymbolicLink(Path.Combine(tree, "link"), "/etc/passwd");
			var archive = Path.Combine(tree, "self.zip");
			var result = await service.CreateAsync([tree], archive);
			Assert.IsTrue(result.Succeeded, result.Error);
			Assert.AreEqual(1, result.ItemsSkipped);
			var names = (await service.ListAsync(archive)).Entries.Select(e => e.Path).ToList();
			Assert.IsFalse(names.Any(n => n.Contains("link") || n.Contains("self.zip")));
		}

		[TestMethod]
		public async Task CreateReportsUnreadableItemsAndCanCancel()
		{
			var tree = MakeTree();
			var locked = Path.Combine(tree, "locked.txt");
			File.WriteAllText(locked, "x");
			File.SetUnixFileMode(locked, 0);
			if (Environment.UserName == "root")
				Assert.Inconclusive("root can read everything");

			IReadOnlyList<string>? reported = null;
			var cancelled = await service.CreateAsync([tree], Path.Combine(Work, "c.zip"), new ArchiveCreateOptions
			{
				ConfirmSkipped = (items, _) => { reported = items; return Task.FromResult(false); },
			});
			Assert.IsTrue(cancelled.Cancelled);
			Assert.AreEqual(locked, reported!.Single());
			Assert.IsFalse(File.Exists(Path.Combine(Work, "c.zip")));

			var skipped = await service.CreateAsync([tree], Path.Combine(Work, "d.zip"));
			Assert.IsTrue(skipped.Succeeded);
			Assert.AreEqual(1, skipped.ItemsSkipped);
		}

		[TestMethod]
		public async Task CreateCancellationLeavesNoFiles()
		{
			var tree = MakeTree();
			using var cts = new CancellationTokenSource();
			cts.Cancel();
			var archive = Path.Combine(Work, "c.zip");
			var result = await service.CreateAsync([tree], archive, null, cts.Token);
			Assert.IsTrue(result.Cancelled);
			Assert.AreEqual(0, Directory.GetFiles(Work, "*.zip").Length + Directory.GetFiles(Work, ".*.tmp").Length);
		}

		[TestMethod]
		public async Task SevenZipIsOnlyOfferedWhenBinaryExists()
		{
			Assert.IsFalse(new LinuxArchiveService(new FakeSevenZip(null)).CreatableFormats.Contains(ArchiveFormat.SevenZip));
			Assert.IsTrue(new LinuxArchiveService(new FakeSevenZip("/usr/bin/7z")).CreatableFormats.Contains(ArchiveFormat.SevenZip));

			var tree = MakeTree();
			var result = await new LinuxArchiveService(new FakeSevenZip(null)).CreateAsync([tree], Path.Combine(Work, "a.7z"), new ArchiveCreateOptions { Format = ArchiveFormat.SevenZip });
			Assert.IsFalse(result.Succeeded);
		}

		[TestMethod]
		public async Task SevenZipUsesRunnerWithoutShellAndSafeArguments()
		{
			var tree = MakeTree();
			var fake = new FakeSevenZip("/usr/bin/7z");
			var archive = Path.Combine(Work, "a.7z");
			var result = await new LinuxArchiveService(fake).CreateAsync([tree, Path.Combine(Work, "x")], archive, new ArchiveCreateOptions { Format = ArchiveFormat.SevenZip });
			// source "x" does not exist, so it fails before running
			Assert.IsFalse(result.Succeeded);

			File.WriteAllText(Path.Combine(Work, "-rf"), "x");
			result = await new LinuxArchiveService(fake).CreateAsync([tree, Path.Combine(Work, "-rf")], archive, new ArchiveCreateOptions { Format = ArchiveFormat.SevenZip });
			Assert.IsTrue(result.Succeeded, result.Error);
			CollectionAssert.Contains(fake.Arguments.ToList(), "--");
			CollectionAssert.Contains(fake.Arguments.ToList(), "./-rf");
			CollectionAssert.Contains(fake.Arguments.ToList(), "-spd");
			Assert.AreEqual(Work, fake.WorkingDirectory);
		}

		[TestMethod]
		public async Task SevenZipRoundTripWithRealBinary()
		{
			var runner = new SevenZipProcessRunner();
			if (runner.FindBinary() is null)
				Assert.Inconclusive("7z is not installed");

			var tree = MakeTree();
			var real = new LinuxArchiveService(runner);
			var archive = Path.Combine(Work, "docs.7z");
			var created = await real.CreateAsync([tree], archive, new ArchiveCreateOptions { Format = ArchiveFormat.SevenZip });
			Assert.IsTrue(created.Succeeded, created.Error);

			var extracted = await real.ExtractAsync(archive, Out);
			Assert.IsTrue(extracted.Succeeded, extracted.Error);
			Assert.AreEqual("alpha", File.ReadAllText(Path.Combine(Out, "Documents", "a.txt")));
			Assert.AreEqual("odd", File.ReadAllText(Path.Combine(Out, "Documents", "@odd -name.txt")));
			Assert.IsTrue((await real.TestAsync(archive)).Succeeded);
		}

		[TestMethod]
		public async Task ExtractsArchivesMadeBySystemTools()
		{
			var tree = MakeTree();
			foreach (var (tool, args, name) in new[]
			{
				("tar", "-cJf", "t.tar.xz"),
				("tar", "-czf", "t.tgz"),
				("zip", "-qr", "t.zip"),
			})
			{
				if (!Run(tool, Work, [args, name, "Documents"]))
					continue;

				var destination = Path.Combine(Out, name);
				var result = await service.ExtractAsync(Path.Combine(Work, name), destination);
				Assert.IsTrue(result.Succeeded, name + ": " + result.Error);
				Assert.AreEqual("alpha", File.ReadAllText(Path.Combine(destination, "Documents", "a.txt")));
			}
		}

		// ---- zip slip and path attacks ----

		[TestMethod]
		[DataRow("../evil.txt")]
		[DataRow("a/../../evil.txt")]
		[DataRow("a/b/../../../evil.txt")]
		[DataRow("..\\evil.txt")]
		[DataRow("a\\..\\..\\evil.txt")]
		[DataRow("/tmp/files-archive-absolute-evil.txt")]
		[DataRow("\\evil.txt")]
		[DataRow("C:\\evil.txt")]
		[DataRow("C:/evil.txt")]
		public async Task ZipSlipEntriesAreRefused(string entryName)
		{
			var zip = ZipWith(Path.Combine(Work, "slip.zip"), ("ok.txt", Bytes("fine")), (entryName, Bytes("pwned")));

			var result = await service.ExtractAsync(zip, Out);

			Assert.IsFalse(result.Succeeded);
			Assert.IsNotNull(result.Error);
			Assert.IsFalse(File.Exists("/tmp/files-archive-absolute-evil.txt"));
			AssertNothingEscaped();
		}

		[TestMethod]
		public async Task EntryNamedNulIsRefused()
		{
			var zip = ZipWith(Path.Combine(Work, "nul.zip"), ("a\0b.txt", Bytes("x")));
			var result = await service.ExtractAsync(zip, Out);
			Assert.IsFalse(result.Succeeded);
			AssertNothingEscaped();
		}

		[TestMethod]
		public async Task ZipSlipInTarIsRefused()
		{
			var tar = Path.Combine(Work, "slip.tar");
			using (var stream = File.Create(tar))
			using (var writer = new TarWriter(stream))
			{
				var entry = new PaxTarEntry(TarEntryType.RegularFile, "../evil.txt") { DataStream = new MemoryStream(Bytes("pwned")) };
				writer.WriteEntry(entry);
			}

			var result = await service.ExtractAsync(tar, Out);
			Assert.IsFalse(result.Succeeded);
			AssertNothingEscaped();
		}

		[TestMethod]
		public void ValidatorNormalizesHarmlessNames()
		{
			Assert.AreEqual("a/b.txt", ArchivePathValidator.NormalizeEntryName("./a//b.txt"));
			Assert.AreEqual("a/b", ArchivePathValidator.NormalizeEntryName("a\\b/"));
			Assert.AreEqual(string.Empty, ArchivePathValidator.NormalizeEntryName("./"));
			Assert.AreEqual("a..b/..c", ArchivePathValidator.NormalizeEntryName("a..b/..c"));
			Assert.ThrowsExactly<ArchiveSecurityException>(() => ArchivePathValidator.NormalizeEntryName(new string('x', 300)));
		}

		[TestMethod]
		public void ValidatorLinkTargets()
		{
			Assert.IsTrue(ArchivePathValidator.IsSafeLinkTarget("a/l", "b.txt"));
			Assert.IsTrue(ArchivePathValidator.IsSafeLinkTarget("a/l", "../b.txt"));
			Assert.IsFalse(ArchivePathValidator.IsSafeLinkTarget("a/l", "../../b.txt"));
			Assert.IsFalse(ArchivePathValidator.IsSafeLinkTarget("l", "../b.txt"));
			Assert.IsFalse(ArchivePathValidator.IsSafeLinkTarget("l", "/etc/passwd"));
			Assert.IsFalse(ArchivePathValidator.IsSafeLinkTarget("l", "a/../../b"));
		}

		// ---- symlinks ----

		private string TarWithLinks(string name, params (string Name, string Target)[] links)
		{
			var tar = Path.Combine(Work, name);
			using var stream = File.Create(tar);
			using var writer = new TarWriter(stream);
			foreach (var (linkName, target) in links)
				writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, linkName) { LinkName = target });

			writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "real.txt") { DataStream = new MemoryStream(Bytes("real")) });
			return tar;
		}

		[TestMethod]
		public async Task SymlinksPointingOutsideAreNotExtracted()
		{
			var tar = TarWithLinks("links.tar", ("abs", "/etc"), ("up", "../../outside"), ("inside", "real.txt"));

			var result = await service.ExtractAsync(tar, Out);

			Assert.IsTrue(result.Succeeded, result.Error);
			Assert.AreEqual(2, result.ItemsSkipped);
			Assert.IsFalse(File.Exists(Path.Combine(Out, "abs")) || Directory.Exists(Path.Combine(Out, "abs")) || new FileInfo(Path.Combine(Out, "abs")).LinkTarget is not null);
			Assert.IsNull(new FileInfo(Path.Combine(Out, "up")).LinkTarget);
			Assert.AreEqual("real.txt", new FileInfo(Path.Combine(Out, "inside")).LinkTarget);
			Assert.AreEqual("real", File.ReadAllText(Path.Combine(Out, "inside")));
		}

		[TestMethod]
		public async Task WritingThroughAnExtractedSymlinkIsRefused()
		{
			var tar = Path.Combine(Work, "through.tar");
			using (var stream = File.Create(tar))
			using (var writer = new TarWriter(stream))
			{
				writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "sub"));
				writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "d") { LinkName = "sub" });
				writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "d/evil.txt") { DataStream = new MemoryStream(Bytes("x")) });
			}

			var result = await service.ExtractAsync(tar, Out);

			Assert.IsFalse(result.Succeeded);
			AssertNothingEscaped();
		}

		// ---- zip bombs ----

		private string BombZip(int megabytes, int files = 1)
		{
			var path = Path.Combine(Work, "bomb.zip");
			using var stream = File.Create(path);
			using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
			var chunk = new byte[1024 * 1024];
			for (var f = 0; f < files; f++)
			{
				using var entry = zip.CreateEntry($"zeros{f}.bin", CompressionLevel.SmallestSize).Open();
				for (var i = 0; i < megabytes; i++)
					entry.Write(chunk);
			}

			return path;
		}

		[TestMethod]
		public async Task TotalSizeLimitRefusesAndOverrideContinues()
		{
			var zip = BombZip(8);
			var limits = new ArchiveLimits(MaxTotalBytes: 2 * 1024 * 1024, MaxEntries: 0, MaxRatio: 0);

			var refused = await service.ExtractAsync(zip, Out, new ArchiveExtractOptions { Limits = limits });
			Assert.IsFalse(refused.Succeeded);
			StringAssert.Contains(refused.Error, "bytes");
			Assert.AreEqual(0, Directory.GetFileSystemEntries(Out).Length);

			ArchiveLimitViolation? asked = null;
			var allowed = await service.ExtractAsync(zip, Out, new ArchiveExtractOptions
			{
				Limits = limits,
				LimitExceeded = (v, _) => { asked = v; return Task.FromResult(true); },
			});
			Assert.IsTrue(allowed.Succeeded, allowed.Error);
			Assert.AreEqual("bytes", asked!.Limit);
			Assert.AreEqual(8L * 1024 * 1024, new FileInfo(Path.Combine(Out, "zeros0.bin")).Length);
		}

		[TestMethod]
		public async Task DecliningTheOverrideStopsTheExtraction()
		{
			var zip = BombZip(4);
			var result = await service.ExtractAsync(zip, Out, new ArchiveExtractOptions
			{
				Limits = new ArchiveLimits(MaxTotalBytes: 1024 * 1024, MaxEntries: 0, MaxRatio: 0),
				LimitExceeded = (_, _) => Task.FromResult(false),
			});
			Assert.IsFalse(result.Succeeded);
			Assert.AreEqual(0, Directory.GetFileSystemEntries(Out).Length);
		}

		[TestMethod]
		public async Task EntryCountLimitRefuses()
		{
			var zip = ZipWith(Path.Combine(Work, "many.zip"), Enumerable.Range(0, 20).Select(i => ($"f{i}.txt", Bytes("x"))).ToArray());
			var result = await service.ExtractAsync(zip, Out, new ArchiveExtractOptions { Limits = new ArchiveLimits(0, MaxEntries: 10, 0) });
			Assert.IsFalse(result.Succeeded);
			StringAssert.Contains(result.Error, "entries");
			Assert.AreEqual(0, Directory.GetFileSystemEntries(Out).Length);
		}

		[TestMethod]
		public async Task CompressionRatioLimitRefuses()
		{
			var zip = BombZip(8);
			var result = await service.ExtractAsync(zip, Out, new ArchiveExtractOptions { Limits = new ArchiveLimits(0, 0, MaxRatio: 50, RatioMinBytes: 1024 * 1024) });
			Assert.IsFalse(result.Succeeded);
			StringAssert.Contains(result.Error, "ratio");
			Assert.AreEqual(0, Directory.GetFileSystemEntries(Out).Length);
		}

		[TestMethod]
		public async Task DefaultLimitsAllowNormalArchives()
		{
			var tree = MakeTree();
			var archive = Path.Combine(Work, "n.zip");
			await service.CreateAsync([tree], archive);
			Assert.IsTrue((await service.ExtractAsync(archive, Out)).Succeeded);
		}

		// ---- conflicts ----

		private async Task<string> ArchiveWithAText(string content)
		{
			var zip = ZipWith(Path.Combine(Work, "c.zip"), ("dir/a.txt", Bytes(content)), ("new.txt", Bytes("new")));
			await Task.CompletedTask;
			return zip;
		}

		private void ExistingFile()
		{
			Directory.CreateDirectory(Path.Combine(Out, "dir"));
			File.WriteAllText(Path.Combine(Out, "dir", "a.txt"), "old");
		}

		[TestMethod]
		public async Task ExistingFilesAreNeverOverwrittenWithoutCallback()
		{
			ExistingFile();
			var result = await service.ExtractAsync(await ArchiveWithAText("incoming"), Out);
			Assert.IsTrue(result.Succeeded, result.Error);
			Assert.AreEqual(1, result.ItemsSkipped);
			Assert.AreEqual("old", File.ReadAllText(Path.Combine(Out, "dir", "a.txt")));
			Assert.AreEqual("new", File.ReadAllText(Path.Combine(Out, "new.txt")));
		}

		[TestMethod]
		public async Task ConflictCallbackDecides()
		{
			var zip = await ArchiveWithAText("incoming");

			ExistingFile();
			ArchiveConflict? seen = null;
			await service.ExtractAsync(zip, Out, new ArchiveExtractOptions
			{
				ResolveConflict = (c, _) => { seen = c; return Task.FromResult(new ConflictResolution(ConflictAction.Overwrite)); },
			});
			Assert.AreEqual("dir/a.txt", seen!.EntryPath);
			Assert.AreEqual("incoming", File.ReadAllText(Path.Combine(Out, "dir", "a.txt")));

			File.WriteAllText(Path.Combine(Out, "dir", "a.txt"), "old");
			await service.ExtractAsync(zip, Out, new ArchiveExtractOptions
			{
				ResolveConflict = (_, _) => Task.FromResult(new ConflictResolution(ConflictAction.KeepBoth)),
			});
			Assert.AreEqual("old", File.ReadAllText(Path.Combine(Out, "dir", "a.txt")));
			Assert.AreEqual("incoming", File.ReadAllText(Path.Combine(Out, "dir", "a (2).txt")));
		}

		[TestMethod]
		public async Task ConflictCancelAbortsAndApplyToAllAsksOnce()
		{
			var zip = await ArchiveWithAText("incoming");

			ExistingFile();
			var cancelled = await service.ExtractAsync(zip, Out, new ArchiveExtractOptions
			{
				ResolveConflict = (_, _) => Task.FromResult(new ConflictResolution(ConflictAction.Cancel)),
			});
			Assert.IsTrue(cancelled.Cancelled);
			Assert.AreEqual("old", File.ReadAllText(Path.Combine(Out, "dir", "a.txt")));
			Assert.AreEqual(0, Directory.GetFileSystemEntries(Out).Count(p => Path.GetFileName(p).StartsWith(".files-extract")));

			var asked = 0;
			File.WriteAllText(Path.Combine(Out, "new.txt"), "old");
			await service.ExtractAsync(zip, Out, new ArchiveExtractOptions
			{
				ResolveConflict = (_, _) => { asked++; return Task.FromResult(new ConflictResolution(ConflictAction.Skip, ApplyToAll: true)); },
			});
			Assert.AreEqual(1, asked);
		}

		[TestMethod]
		public async Task FolderDestinationIsCreatedAndRemovedWhenExtractionFails()
		{
			var zip = ZipWith(Path.Combine(Work, "slip.zip"), ("../evil.txt", Bytes("x")));
			var destination = Path.Combine(Out, "new-folder");
			var result = await service.ExtractAsync(zip, destination);
			Assert.IsFalse(result.Succeeded);
			Assert.IsFalse(Directory.Exists(destination));
		}

		// ---- passwords, integrity, cancellation ----

		[TestMethod]
		public async Task PasswordProtectedZip()
		{
			var tree = MakeTree();
			if (!Run("zip", Work, ["-q", "-r", "-P", "s3cret", "enc.zip", "Documents"]))
				Assert.Inconclusive("zip is not installed");

			var archive = Path.Combine(Work, "enc.zip");
			Assert.IsTrue(await service.IsEncryptedAsync(archive));
			Assert.IsFalse(await service.IsEncryptedAsync(await ArchiveWithAText("x")));

			await Assert.ThrowsExactlyAsync<ArchivePasswordException>(() => service.ExtractAsync(archive, Out));
			await Assert.ThrowsExactlyAsync<ArchivePasswordException>(() => service.ExtractAsync(archive, Out, new ArchiveExtractOptions { Password = "wrong" }));
			Assert.AreEqual(0, Directory.GetFileSystemEntries(Out).Length);

			var ok = await service.ExtractAsync(archive, Out, new ArchiveExtractOptions { Password = "s3cret" });
			Assert.IsTrue(ok.Succeeded, ok.Error);
			Assert.AreEqual("alpha", File.ReadAllText(Path.Combine(Out, "Documents", "a.txt")));
			Assert.IsTrue((await service.TestAsync(archive, "s3cret")).Succeeded);
		}

		[TestMethod]
		public async Task TestAsyncDetectsCorruption()
		{
			var tree = MakeTree();
			var archive = Path.Combine(Work, "t.zip");
			await service.CreateAsync([tree], archive, new ArchiveCreateOptions { Level = ArchiveCompressionLevel.None });
			Assert.IsTrue((await service.TestAsync(archive)).Succeeded);

			var bytes = await File.ReadAllBytesAsync(archive);
			var marker = Bytes("alpha");
			var index = bytes.AsSpan().IndexOf(marker);
			Assert.IsTrue(index > 0);
			bytes[index] ^= 0xFF;
			await File.WriteAllBytesAsync(archive, bytes);

			var result = await service.TestAsync(archive);
			Assert.IsFalse(result.Succeeded);
		}

		[TestMethod]
		public async Task ExtractionCancellationLeavesNothing()
		{
			var zip = BombZip(2, files: 3);
			using var cts = new CancellationTokenSource();
			cts.Cancel();
			var result = await service.ExtractAsync(zip, Out, null, cts.Token).ContinueWith(t => t.IsCanceled ? null : t.Result);
			Assert.IsTrue(result is null || result.Cancelled);
			Assert.AreEqual(0, Directory.GetFileSystemEntries(Out).Length);
		}

		[TestMethod]
		public void ExtensionHelpers()
		{
			Assert.AreEqual("docs", service.GetDefaultExtractFolderName("/x/docs.tar.gz"));
			Assert.AreEqual("a.b", service.GetDefaultExtractFolderName("/x/a.b.zip"));
			Assert.IsTrue(service.IsArchiveFileName("/x/a.TGZ"));
			Assert.IsFalse(service.IsArchiveFileName("/x/a.txt"));
			Assert.IsFalse(service.IsArchiveFileName("/x/.zip"));
		}

		[TestMethod]
		public async Task CorruptArchiveFailsCleanly()
		{
			var path = Path.Combine(Work, "junk.zip");
			await File.WriteAllTextAsync(path, "this is not a zip");
			var result = await service.ExtractAsync(path, Out);
			Assert.IsFalse(result.Succeeded);
			Assert.AreEqual(0, Directory.GetFileSystemEntries(Out).Length);
		}

		private static bool Run(string tool, string workingDirectory, IReadOnlyList<string> args)
		{
			try
			{
				var info = new ProcessStartInfo(tool) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
				foreach (var a in args)
					info.ArgumentList.Add(a);

				using var process = Process.Start(info)!;
				process.StandardOutput.ReadToEnd();
				process.StandardError.ReadToEnd();
				process.WaitForExit();
				return process.ExitCode == 0;
			}
			catch (System.ComponentModel.Win32Exception)
			{
				return false;
			}
		}

		private sealed class FakeSevenZip : ISevenZipRunner
		{
			private readonly string? binary;

			public FakeSevenZip(string? binary) => this.binary = binary;

			public IReadOnlyList<string> Arguments { get; private set; } = [];

			public string? WorkingDirectory { get; private set; }

			public string? FindBinary() => binary;

			public Task<(int ExitCode, string Output)> RunAsync(string binary, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
			{
				Arguments = arguments;
				WorkingDirectory = workingDirectory;
				File.WriteAllText(arguments[arguments.ToList().IndexOf("--") + 1], "fake");
				return Task.FromResult((0, string.Empty));
			}
		}
	}
}
