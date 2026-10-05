// Copyright (c) Files Community
// Licensed under the MIT License.

#pragma warning disable CA1416

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Files.Platform.Abstractions.Trash;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Platform.Tests.Trash
{
	public sealed partial class LinuxTrashServiceTests
	{
		private void SetRestoreHook(Action<string, string> hook)
		{
			_service.Watcher.Dispose();
			_service = CreateService(Uid, beforeRestore: hook);
		}

		[TestMethod]
		[DataRow(false, false)]
		[DataRow(true, false)]
		[DataRow(false, true)]
		[DataRow(true, true)]
		public async Task Restore_ParentSwappedAfterContainment_WritesToPinnedFolder(bool topdir, bool replace)
		{
			var parent = Path.Combine(topdir ? _usb : _home, "restore-parent");
			var original = WriteFile(parent, "a.txt", "data");
			var item = Single(await _service.TrashAsync([original])).Item!;
			if (replace)
				WriteFile(parent, "a.txt", "old");
			var outside = Path.Combine(_root, "outside");
			var precious = WriteFile(outside, "a.txt", "keep");
			SetRestoreHook((_, _) =>
			{
				Directory.Move(parent, parent + ".saved");
				Directory.CreateSymbolicLink(parent, outside);
			});

			var result = Single(await _service.RestoreAsync([item], replace ? TrashRestoreConflictBehavior.Replace : TrashRestoreConflictBehavior.Fail));

			Assert.IsTrue(result.Succeeded, result.ErrorMessage);
			Assert.AreEqual("data", File.ReadAllText(Path.Combine(parent + ".saved", "a.txt")));
			Assert.AreEqual("keep", File.ReadAllText(precious));
			Assert.IsFalse(File.Exists(item.TrashedPath));
			Assert.IsFalse(File.Exists(item.TrashId));
		}

		[TestMethod]
		public async Task Restore_TrashFilesSwappedAfterContainment_MovesFromPinnedFolder()
		{
			var original = WriteFile(_home, "a.txt", "data");
			var item = Single(await _service.TrashAsync([original])).Item!;
			var files = Path.GetDirectoryName(item.TrashedPath)!;
			var outside = Path.Combine(_root, "outside");
			var precious = WriteFile(outside, Path.GetFileName(item.TrashedPath), "keep");
			SetRestoreHook((_, _) =>
			{
				Directory.Move(files, files + ".saved");
				Directory.CreateSymbolicLink(files, outside);
			});

			var result = Single(await _service.RestoreAsync([item]));

			Assert.IsTrue(result.Succeeded, result.ErrorMessage);
			Assert.AreEqual("data", File.ReadAllText(original));
			Assert.AreEqual("keep", File.ReadAllText(precious));
			Assert.IsEmpty(Directory.GetFileSystemEntries(files + ".saved"));
		}

		[TestMethod]
		public async Task Restore_TrashRootSwappedAfterOpen_CleansPinnedInfoAndCache()
		{
			var original = Path.Combine(_home, "tree");
			WriteFile(original, "a", "data");
			var item = Single(await _service.TrashAsync([original])).Item!;
			var outside = Path.Combine(_root, "outside");
			var preciousInfo = WriteFile(Path.Combine(outside, "info"), Path.GetFileName(item.TrashId), "keep info");
			var preciousCache = WriteFile(outside, "directorysizes", "keep cache");
			SetRestoreHook((_, _) =>
			{
				Directory.Move(HomeTrash, HomeTrash + ".saved");
				Directory.CreateSymbolicLink(HomeTrash, outside);
			});

			var result = Single(await _service.RestoreAsync([item]));

			Assert.IsTrue(result.Succeeded, result.ErrorMessage);
			Assert.AreEqual("data", File.ReadAllText(Path.Combine(original, "a")));
			Assert.IsEmpty(Directory.GetFileSystemEntries(Path.Combine(HomeTrash + ".saved", "info")));
			Assert.AreEqual(string.Empty, File.ReadAllText(Path.Combine(HomeTrash + ".saved", "directorysizes")));
			Assert.AreEqual("keep info", File.ReadAllText(preciousInfo));
			Assert.AreEqual("keep cache", File.ReadAllText(preciousCache));
		}

		[TestMethod]
		public async Task Restore_DestinationAppearsBeforeRename_DoesNotReplace()
		{
			var original = WriteFile(_home, "a.txt", "data");
			var item = Single(await _service.TrashAsync([original])).Item!;
			SetRestoreHook((_, destination) => File.WriteAllText(destination, "keep"));

			var result = Single(await _service.RestoreAsync([item]));

			Assert.IsFalse(result.Succeeded);
			Assert.AreEqual("keep", File.ReadAllText(original));
			Assert.AreEqual("data", File.ReadAllText(item.TrashedPath));
			Assert.IsTrue(File.Exists(item.TrashId));
		}

		[TestMethod]
		public async Task Restore_SymlinkedDestinationInsideVolume_IsAccepted()
		{
			var item = await TrashOnUsb();
			var real = Path.Combine(_usb, "real");
			Directory.CreateDirectory(real);
			Directory.CreateSymbolicLink(Path.Combine(_usb, "link"), real);
			TamperInfo(item.TrashId, "link/a.txt");

			var result = Single(await _service.RestoreAsync([item]));

			Assert.IsTrue(result.Succeeded, result.ErrorMessage);
			Assert.AreEqual("hello", File.ReadAllText(Path.Combine(real, "a.txt")));
		}

		[TestMethod]
		public async Task Restore_ReplaceDirectoryContainingSymlink_LeavesOutsideTreeIntact()
		{
			var original = Path.Combine(_home, "tree");
			WriteFile(original, "a", "data");
			var item = Single(await _service.TrashAsync([original])).Item!;
			var outside = Path.Combine(_root, "outside");
			var precious = WriteFile(outside, "precious", "keep");
			Directory.CreateDirectory(original);
			WriteFile(original, "old");
			Directory.CreateSymbolicLink(Path.Combine(original, "link"), outside);

			var result = Single(await _service.RestoreAsync([item], TrashRestoreConflictBehavior.Replace));

			Assert.IsTrue(result.Succeeded, result.ErrorMessage);
			Assert.AreEqual("data", File.ReadAllText(Path.Combine(original, "a")));
			Assert.IsFalse(File.Exists(Path.Combine(original, "old")));
			Assert.AreEqual("keep", File.ReadAllText(precious));
		}
	}
}
