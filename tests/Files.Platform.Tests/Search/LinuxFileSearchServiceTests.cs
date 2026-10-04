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
	}
}
