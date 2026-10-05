// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Storage.Storables;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OwlCore.Storage;
using OwlCore.Storage.System.IO;

namespace Files.Platform.Tests.Storables
{
	[TestClass]
	public sealed class LocalStorableRouteTests
	{
		private readonly LocalStorableRoute _route = new();
		private string _root = null!;

		[TestInitialize]
		public void Setup()
		{
			_root = Path.Combine(Path.GetTempPath(), "files-storables-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_root);
		}

		[TestCleanup]
		public void Cleanup()
		{
			Directory.Delete(_root, recursive: true);
		}

		[TestMethod]
		[DataRow("relative/path")]
		[DataRow("Home")]
		[DataRow("ftp://host/file")]
		[DataRow("Shell:RecycleBinFolder")]
		[DataRow("")]
		public void CanResolve_RejectsNonLocalPaths(string path)
		{
			Assert.IsFalse(_route.CanResolve(path));
		}

		[TestMethod]
		public void CanResolve_DoesNotRequireExistence()
		{
			Assert.IsTrue(_route.CanResolve(Path.Combine(_root, "missing")));
			Assert.IsFalse(_route.CanResolve(_root + "/a\0b"));
		}

		[TestMethod]
		public async Task TryGet_Folder_ReturnsSystemFolder()
		{
			var path = Path.Combine(_root, "dir");
			Directory.CreateDirectory(path);
			File.WriteAllText(Path.Combine(path, "child.txt"), "data");

			var folder = await _route.TryGetAsync(path) as SystemFolder;

			Assert.IsNotNull(folder);
			Assert.AreEqual(path, folder.Id);
			Assert.AreEqual("dir", folder.Name);
			var children = new List<string>();
			await foreach (var child in folder.GetItemsAsync())
				children.Add(child.Name);
			CollectionAssert.AreEqual(new[] { "child.txt" }, children);
		}

		[TestMethod]
		public async Task TryGet_File_ReturnsReadableSystemFile()
		{
			var path = Path.Combine(_root, "file.txt");
			File.WriteAllText(path, "data");

			var file = await _route.TryGetAsync(path) as SystemFile;

			Assert.IsNotNull(file);
			Assert.AreEqual(path, file.Id);
			Assert.AreEqual("file.txt", file.Name);
			using var stream = await file.OpenReadAsync();
			using var reader = new StreamReader(stream);
			Assert.AreEqual("data", await reader.ReadToEndAsync());
		}

		[TestMethod]
		public async Task TryGet_TrailingSeparator_IsTrimmed()
		{
			var path = Path.Combine(_root, "dir");
			Directory.CreateDirectory(path);

			var folder = await _route.TryGetAsync(path + Path.DirectorySeparatorChar) as IFolder;

			Assert.IsNotNull(folder);
			Assert.AreEqual(path, folder.Id);
			Assert.AreEqual("dir", folder.Name);
		}

		[TestMethod]
		public async Task TryGet_Root_ReturnsFolder()
		{
			var root = Path.GetPathRoot(_root)!;

			Assert.IsInstanceOfType<IFolder>(await _route.TryGetAsync(root));
		}

		[TestMethod]
		public async Task TryGet_Missing_ReturnsNull()
		{
			Assert.IsNull(await _route.TryGetAsync(Path.Combine(_root, "missing")));
			Assert.IsNull(await _route.TryGetAsync(Path.Combine(_root, "missing", "deeper")));
		}

		[TestMethod]
		public async Task TryGet_NonLocalPath_ReturnsNull()
		{
			Assert.IsNull(await _route.TryGetAsync("relative"));
		}

		[TestMethod]
		public async Task TryGet_SymlinkToFolder_ReturnsFolder()
		{
			var target = Path.Combine(_root, "target");
			Directory.CreateDirectory(target);
			var link = Path.Combine(_root, "link");
			Directory.CreateSymbolicLink(link, target);

			var folder = await _route.TryGetAsync(link) as IFolder;

			Assert.IsNotNull(folder);
			Assert.AreEqual(link, folder.Id);
		}

		[TestMethod]
		public async Task TryGet_DanglingSymlink_ReturnsFile()
		{
			var link = Path.Combine(_root, "dangling");
			File.CreateSymbolicLink(link, Path.Combine(_root, "missing"));

			Assert.IsInstanceOfType<IFile>(await _route.TryGetAsync(link));
		}

		[TestMethod]
		public async Task TryGet_Canceled_Throws()
		{
			await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => _route.TryGetAsync(_root, new CancellationToken(canceled: true)));
		}

		[TestMethod]
		public async Task Resolver_WithLocalRoute_ResolvesTempDir()
		{
			var resolver = new StorableResolver([_route]);

			Assert.IsInstanceOfType<IFolder>(await resolver.TryGetAsync(_root));
			Assert.IsNull(await resolver.TryGetAsync("relative"));
		}
	}
}
