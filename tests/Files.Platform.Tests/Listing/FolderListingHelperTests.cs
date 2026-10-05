// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Files.Platform.Linux.Enumeration;
using Files.Platform.Linux.Mime;
using Files.Platform.Linux.Watching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Platform.Tests.Listing
{
	[TestClass]
	public sealed class FolderListingHelperTests
	{
		private string _root = null!;

		[TestInitialize]
		public void Setup()
		{
			_root = Path.Combine(Path.GetTempPath(), "files-listing-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_root);
		}

		[TestCleanup]
		public void Cleanup()
		{
			try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
		}

		[TestMethod]
		public void Batch_DeduplicatesPaths()
		{
			var batch = new FolderChangeBatch();
			batch.Add("/a/x");
			batch.Add("/a/x");
			batch.Add("/a/y");

			var snapshot = batch.Drain();
			Assert.IsFalse(snapshot.FullRefresh);
			Assert.AreEqual(2, snapshot.Paths.Count);
			Assert.IsTrue(batch.IsEmpty);
		}

		[TestMethod]
		public void Batch_CollapsesRenameChains()
		{
			var batch = new FolderChangeBatch();
			batch.AddRename("/a/1", "/a/2");
			batch.AddRename("/a/2", "/a/3");

			var snapshot = batch.Drain();
			Assert.AreEqual(1, snapshot.Renames.Count);
			Assert.AreEqual("/a/3", snapshot.Renames["/a/1"]);
		}

		[TestMethod]
		public void Batch_RenameBackToOriginalIsNoRename()
		{
			var batch = new FolderChangeBatch();
			batch.AddRename("/a/1", "/a/2");
			batch.AddRename("/a/2", "/a/1");

			Assert.AreEqual(0, batch.Drain().Renames.Count);
		}

		[TestMethod]
		public void Batch_FallsBackToFullRefreshOverThreshold()
		{
			var batch = new FolderChangeBatch(overflowThreshold: 10);
			for (var i = 0; i < 11; i++)
				batch.Add("/a/" + i);

			var snapshot = batch.Drain();
			Assert.IsTrue(snapshot.FullRefresh);
			Assert.AreEqual(0, snapshot.Paths.Count);

			batch.Add("/a/next");
			Assert.IsFalse(batch.Drain().FullRefresh);
		}

		[TestMethod]
		public void Batch_RescanRequestsFullRefresh()
		{
			var batch = new FolderChangeBatch();
			batch.RequestFullRefresh();
			Assert.IsTrue(batch.Drain().FullRefresh);
		}

		private static IReadOnlyList<FolderChangeOp> Plan(FolderChangeBatch batch, string[] listed, string[] existing)
			=> FolderChangeReconciler.Plan(batch.Drain(), listed.Contains, existing.Contains);

		[TestMethod]
		public void Reconciler_MapsStateToOperations()
		{
			var batch = new FolderChangeBatch();
			foreach (var p in new[] { "/f/new", "/f/gone", "/f/kept", "/f/ghost" })
				batch.Add(p);

			var ops = Plan(batch, listed: ["/f/gone", "/f/kept"], existing: ["/f/new", "/f/kept"]);

			CollectionAssert.AreEqual(
				new[] { FolderChangeKind.Remove, FolderChangeKind.Update, FolderChangeKind.Add },
				ops.Select(o => o.Kind).ToArray());
			Assert.AreEqual("/f/gone", ops[0].Path);
			Assert.AreEqual("/f/kept", ops[1].Path);
			Assert.AreEqual("/f/new", ops[2].Path);
		}

		[TestMethod]
		public void Reconciler_CreateThenDeleteIsNothing()
		{
			var batch = new FolderChangeBatch();
			batch.Add("/f/tmp");
			batch.Add("/f/tmp");

			Assert.AreEqual(0, Plan(batch, listed: [], existing: []).Count);
		}

		[TestMethod]
		public void Reconciler_RenameMovesTheExistingItem()
		{
			var batch = new FolderChangeBatch();
			batch.AddRename("/f/a", "/f/b");

			var ops = Plan(batch, listed: ["/f/a"], existing: ["/f/b"]);
			Assert.AreEqual(1, ops.Count);
			Assert.AreEqual(FolderChangeKind.Rename, ops[0].Kind);
			Assert.AreEqual("/f/a", ops[0].Path);
			Assert.AreEqual("/f/b", ops[0].NewPath);
		}

		[TestMethod]
		public void Reconciler_RenameOverwriteFallsBackToRemoveAndUpdate()
		{
			var batch = new FolderChangeBatch();
			batch.AddRename("/f/a", "/f/b");

			var ops = Plan(batch, listed: ["/f/a", "/f/b"], existing: ["/f/b"]);
			CollectionAssert.AreEqual(new[] { FolderChangeKind.Remove, FolderChangeKind.Update }, ops.Select(o => o.Kind).ToArray());
		}

		[TestMethod]
		public void EntryReader_ReadsFilesFoldersAndLinks()
		{
			var file = Path.Combine(_root, "a.txt");
			File.WriteAllText(file, "hello");
			var dir = Path.Combine(_root, "d");
			Directory.CreateDirectory(dir);
			var dirLink = Path.Combine(_root, "dl");
			Directory.CreateSymbolicLink(dirLink, dir);
			var broken = Path.Combine(_root, "broken");
			File.CreateSymbolicLink(broken, "does-not-exist");

			var f = FileSystemEntryReader.TryRead(file)!;
			Assert.IsFalse(f.IsDirectory);
			Assert.IsFalse(f.IsSymlink);
			Assert.AreEqual(5, f.Length);

			var l = FileSystemEntryReader.TryRead(dirLink)!;
			Assert.IsTrue(l.IsDirectory);
			Assert.IsTrue(l.IsSymlink);
			Assert.IsFalse(l.IsBrokenSymlink);
			Assert.AreEqual(dir, l.LinkTarget);

			var b = FileSystemEntryReader.TryRead(broken)!;
			Assert.IsTrue(b.IsSymlink);
			Assert.IsTrue(b.IsBrokenSymlink);
			Assert.AreEqual("does-not-exist", b.LinkTarget);

			Assert.IsNull(FileSystemEntryReader.TryRead(Path.Combine(_root, "missing")));
		}

		[TestMethod]
		public async System.Threading.Tasks.Task EntryReader_MatchesTheEnumerator()
		{
			var file = Path.Combine(_root, ".hidden");
			File.WriteAllText(file, "x");
			var link = Path.Combine(_root, "link");
			File.CreateSymbolicLink(link, file);

			var listed = new Dictionary<string, Files.Platform.Abstractions.Enumeration.FileSystemEntryInfo>();
			await foreach (var e in new LinuxFileSystemEnumerator().EnumerateAsync(_root, new Files.Platform.Abstractions.Enumeration.FileSystemEnumerationOptions { IncludeHidden = true }))
				listed[e.FullPath] = e;
			foreach (var path in new[] { file, link })
			{
				var read = FileSystemEntryReader.TryRead(path)!;
				var expected = listed[path];
				Assert.AreEqual(expected.Name, read.Name);
				Assert.AreEqual(expected.IsDirectory, read.IsDirectory);
				Assert.AreEqual(expected.IsSymlink, read.IsSymlink);
				Assert.AreEqual(expected.IsBrokenSymlink, read.IsBrokenSymlink);
				Assert.AreEqual(expected.LinkTarget, read.LinkTarget);
				Assert.AreEqual(expected.IsHidden, read.IsHidden);
			}
		}

		[TestMethod]
		public void DesktopDisplay_ReadsLocalizedNameAndIcon()
		{
			var path = Path.Combine(_root, "app.desktop");
			File.WriteAllText(path, "[Desktop Entry]\nType=Application\nName=App\nName[de]=Anwendung\nExec=/bin/true\nIcon=app-icon\n");

			var de = DesktopEntryDisplay.TryRead(path, new CultureInfo("de-DE"))!;
			Assert.AreEqual("Anwendung", de.Name);
			Assert.AreEqual("app-icon", de.Icon);
			Assert.AreEqual("App", DesktopEntryDisplay.TryRead(path, CultureInfo.InvariantCulture)!.Name);
		}

		[TestMethod]
		public void DesktopDisplay_RejectsAmbiguousOrOversizedFiles()
		{
			var dup = Path.Combine(_root, "dup.desktop");
			File.WriteAllText(dup, "[Desktop Entry]\nType=Application\nName=A\nExec=/bin/true\nExec=/bin/false\n");
			Assert.IsNull(DesktopEntryDisplay.TryRead(dup, CultureInfo.InvariantCulture));

			var big = Path.Combine(_root, "big.desktop");
			File.WriteAllText(big, "[Desktop Entry]\nType=Application\nName=A\nExec=/bin/true\n#" + new string('x', DesktopEntryDisplay.MaxFileSize));
			Assert.IsNull(DesktopEntryDisplay.TryRead(big, CultureInfo.InvariantCulture));

			Assert.IsNull(DesktopEntryDisplay.TryRead(Path.Combine(_root, "none.desktop"), CultureInfo.InvariantCulture));
		}

		[TestMethod]
		public void DesktopDisplay_EscapesControlCharactersInName()
		{
			var path = Path.Combine(_root, "evil.desktop");
			File.WriteAllText(path, "[Desktop Entry]\nType=Application\nName=Safe\\u202Eevil\nExec=/bin/true\n");
			var info = DesktopEntryDisplay.TryRead(path, CultureInfo.InvariantCulture)!;
			Assert.IsFalse(info.Name.Any(char.IsControl));
		}
	}
}
