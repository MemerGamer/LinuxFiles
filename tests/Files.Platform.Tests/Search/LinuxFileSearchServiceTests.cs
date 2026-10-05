// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Search;
using Files.Platform.Linux.Enumeration;
using Files.Platform.Linux.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Search
{
	[TestClass]
	[UnsupportedOSPlatform("windows")]
	public sealed class LinuxFileSearchServiceTests
	{
		private string _root = null!;
		private readonly LinuxFileSearchService _service = new(new LinuxFileSystemEnumerator());

		[TestInitialize]
		public void Setup()
		{
			_root = Path.Combine(Path.GetTempPath(), "files-search-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path.Combine(_root, "a", "b", "c"));
			File.WriteAllText(Path.Combine(_root, "Report.TXT"), "hello world\nsecond Needle line");
			File.WriteAllText(Path.Combine(_root, "a", "notes.md"), "nothing here");
			File.WriteAllText(Path.Combine(_root, "a", "b", "c", "deep-report.txt"), "needle");
			File.WriteAllText(Path.Combine(_root, ".hidden-report"), "x");
			File.WriteAllBytes(Path.Combine(_root, "bin.dat"), [1, 2, 0, 3, (byte)'n', (byte)'e', (byte)'e', (byte)'d', (byte)'l', (byte)'e']);
		}

		[TestCleanup]
		public void Cleanup()
		{
			try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
		}

		private async Task<List<string>> RunAsync(string pattern, FileSearchOptions? options = null, CancellationToken token = default)
		{
			var list = new List<string>();
			await foreach (var m in _service.SearchAsync(_root, pattern, options, token))
				list.Add(Path.GetRelativePath(_root, m.Entry.FullPath));
			list.Sort(StringComparer.Ordinal);
			return list;
		}

		[TestMethod]
		public async Task Substring_IsCaseInsensitiveAndRecursive()
		{
			CollectionAssert.AreEqual(new[] { "Report.TXT", "a/b/c/deep-report.txt" }, await RunAsync("REPORT"));
		}

		[TestMethod]
		public async Task Wildcard_IsAnchoredGlob()
		{
			CollectionAssert.AreEqual(new[] { "Report.TXT", "a/b/c/deep-report.txt" }, await RunAsync("*.txt"));
			CollectionAssert.AreEqual(new[] { "a/notes.md" }, await RunAsync("n?tes.*"));
			CollectionAssert.AreEqual(Array.Empty<string>(), await RunAsync("report"[..3] + "*x"));
		}

		[TestMethod]
		public async Task Hidden_FollowsOption()
		{
			CollectionAssert.DoesNotContain(await RunAsync("report"), ".hidden-report");
			CollectionAssert.Contains(await RunAsync("report", new FileSearchOptions { IncludeHidden = true }), ".hidden-report");
		}

		[TestMethod]
		public async Task MaxDepth_PrunesTheWalk()
		{
			CollectionAssert.AreEqual(new[] { "Report.TXT" }, await RunAsync("report", new FileSearchOptions { MaxDepth = 1 }));
		}

		[TestMethod]
		public async Task MaxResults_StopsEarly()
		{
			Assert.HasCount(1, await RunAsync("report", new FileSearchOptions { MaxResults = 1 }));
		}

		[TestMethod]
		public async Task Content_FindsTextAndSkipsBinary()
		{
			var hits = await RunAsync("needle", new FileSearchOptions { SearchContent = true });
			CollectionAssert.AreEqual(new[] { "Report.TXT", "a/b/c/deep-report.txt" }, hits);
		}

		[TestMethod]
		public async Task Content_RespectsSizeCap()
		{
			var hits = await RunAsync("needle", new FileSearchOptions { SearchContent = true, MaxContentFileSize = 8 });
			CollectionAssert.AreEqual(new[] { "a/b/c/deep-report.txt" }, hits);
		}

		[TestMethod]
		public async Task Cancellation_Throws()
		{
			using var cts = new CancellationTokenSource();
			cts.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(() => RunAsync("report", null, cts.Token));
		}

		[TestMethod]
		public async Task SymlinkedFolders_AreNotEntered()
		{
			Directory.CreateSymbolicLink(Path.Combine(_root, "a", "loop"), _root);
			var hits = await RunAsync("report");
			Assert.HasCount(2, hits);
		}

		[TestMethod]
		public async Task ContentSearch_DoesNotBlockOnFifos()
		{
			using (var mk = System.Diagnostics.Process.Start("mkfifo", Path.Combine(_root, "pipe-needle")))
				await mk!.WaitForExitAsync();

			var task = RunAsync("needle", new FileSearchOptions { SearchContent = true });
			Assert.AreSame(task, await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))), "search blocked on a FIFO");
			Assert.DoesNotContain(h => h.EndsWith("pipe-needle", StringComparison.Ordinal), await task);
		}

		[TestMethod]
		public async Task ContentSearch_SkipsSymlinkedFiles()
		{
			File.CreateSymbolicLink(Path.Combine(_root, "link-to-report"), Path.Combine(_root, "Report.TXT"));
			var hits = await RunAsync("Needle", new FileSearchOptions { SearchContent = true });
			Assert.DoesNotContain(h => h.EndsWith("link-to-report", StringComparison.Ordinal), hits);
		}

		[TestMethod]
		public void FindInContent_ReadsAtMostTheByteBudget()
		{
			var path = Path.Combine(_root, "huge.txt");
			using (var f = File.Create(path))
			{
				var block = new byte[64 * 1024];
				Array.Fill(block, (byte)'x'); // one enormous line without a newline
				for (var i = 0; i < 40; i++)
					f.Write(block);
				f.Write("needle"u8);
			}

			Assert.IsNull(LinuxFileSearchService.FindInContent(path, "needle", CancellationToken.None));
		}

		[TestMethod]
		public void FindInContent_FindsMatchInsideLongLine()
		{
			var path = Path.Combine(_root, "long.txt");
			File.WriteAllText(path, new string('x', 20000) + "NeEdLe" + new string('y', 20000));
			Assert.IsNotNull(LinuxFileSearchService.FindInContent(path, "needle", CancellationToken.None));
		}

		[TestMethod]
		public void GitDirectoryResolver_HandlesDirectoryAndLinkedWorktree()
		{
			var main = Path.Combine(_root, "main");
			Directory.CreateDirectory(Path.Combine(main, ".git", "worktrees", "wt"));
			Assert.AreEqual(Path.Combine(main, ".git"), GitDirectoryResolver.Resolve(main)!.Value.GitDir);

			var wt = Path.Combine(_root, "wt");
			Directory.CreateDirectory(wt);
			var gitDir = Path.Combine(main, ".git", "worktrees", "wt");
			File.WriteAllText(Path.Combine(wt, ".git"), "gitdir: " + gitDir + "\n");
			File.WriteAllText(Path.Combine(gitDir, "commondir"), "../..\n");

			var resolved = GitDirectoryResolver.Resolve(wt)!.Value;
			Assert.AreEqual(gitDir, resolved.GitDir);
			Assert.AreEqual(Path.Combine(main, ".git"), resolved.CommonDir);
			Assert.IsNull(GitDirectoryResolver.Resolve(Path.Combine(_root, "a")));
		}

		[TestMethod]
		[SupportedOSPlatform("linux")]
		public void TrustedNativeDirectory_RejectsUnsafeInputsAndLoadsThroughDescriptor()
		{
			var root = Path.Combine(AppContext.BaseDirectory, "trust-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				var rootOwned = "/bin/sh";
				var userOwned = Path.Combine(root, "lib.so");
				File.WriteAllText(userOwned, "x");

				// A library the user (or anyone else) could have written is never linked
				Assert.IsNull(Files.Platform.Linux.Native.TrustedNativeDirectory.Prepare(root, ["Files", "native"], "git2-x.so", userOwned));
				Assert.IsNull(Files.Platform.Linux.Native.TrustedNativeDirectory.Prepare("relative/cache", ["Files"], "x.so", rootOwned));

				// A group-writable cache root is rejected
				File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupWrite);
				Assert.IsNull(Files.Platform.Linux.Native.TrustedNativeDirectory.Prepare(root, ["Files", "native"], "git2-x.so", rootOwned));
				File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

				// A symlinked component of the cache path is not followed
				var real = Path.Combine(root, "real");
				Directory.CreateDirectory(real);
				File.CreateSymbolicLink(Path.Combine(root, "linked"), real);
				Assert.IsNull(Files.Platform.Linux.Native.TrustedNativeDirectory.Prepare(Path.Combine(root, "linked"), ["Files"], "git2-x.so", rootOwned));

				// A missing cache root is created, and the link is reachable through the returned descriptor path
				var dir = Files.Platform.Linux.Native.TrustedNativeDirectory.Prepare(Path.Combine(root, "newcache"), ["Files", "native"], "git2-x.so", rootOwned);
				Assert.IsNotNull(dir);
				StringAssert.StartsWith(dir, "/proc/self/fd/");
				Assert.AreEqual(rootOwned, new FileInfo(Path.Combine(dir, "git2-x.so")).LinkTarget);
				Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.Combine(root, "newcache", "Files", "native")));
			}
			finally
			{
				Directory.Delete(root, recursive: true);
			}
		}

		[TestMethod]
		public async Task GitDirectoryResolver_IgnoresFifoOversizedAndCyclicControlFiles()
		{
			var fifoRepo = Path.Combine(_root, "fifo");
			Directory.CreateDirectory(fifoRepo);
			using (var mk = System.Diagnostics.Process.Start("mkfifo", Path.Combine(fifoRepo, ".git")))
				await mk!.WaitForExitAsync();
			var task = Task.Run(() => GitDirectoryResolver.Resolve(fifoRepo));
			Assert.AreSame(task, await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))), "blocked on a FIFO .git");
			Assert.IsNull(await task);

			var big = Path.Combine(_root, "big");
			Directory.CreateDirectory(big);
			Directory.CreateDirectory(Path.Combine(_root, "target"));
			File.WriteAllText(Path.Combine(big, ".git"), "gitdir: " + Path.Combine(_root, "target") + new string(' ', GitDirectoryResolver.MaxControlFileBytes * 4));
			Assert.IsNull(GitDirectoryResolver.Resolve(big));

			var a = Path.Combine(_root, "cycA");
			var b = Path.Combine(_root, "cycB");
			Directory.CreateDirectory(a);
			Directory.CreateDirectory(b);
			File.WriteAllText(Path.Combine(a, "commondir"), b);
			File.WriteAllText(Path.Combine(b, "commondir"), a);
			var cyc = Path.Combine(_root, "cyc");
			Directory.CreateDirectory(cyc);
			File.WriteAllText(Path.Combine(cyc, ".git"), "gitdir: " + a);
			Assert.IsNull(GitDirectoryResolver.Resolve(cyc));
		}
	}
}
