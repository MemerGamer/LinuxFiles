// Copyright (c) Files Community
// Licensed under the MIT License.

// The Linux platform tests only run on Linux.
#pragma warning disable CA1416

using Files.Platform.Abstractions.Trash;
using Files.Platform.Linux.Native;
using Files.Platform.Linux.Trash;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Trash
{
	public sealed partial class LinuxTrashServiceTests
	{
		private static string TamperInfo(string infoPath, string newPath)
		{
			File.WriteAllText(infoPath, $"[Trash Info]\nPath={newPath}\nDeletionDate=2026-10-04T13:05:09\n");
			return infoPath;
		}

		private async Task<TrashItem> TrashOnUsb(string name = "a.txt")
		{
			var result = Single(await _service.TrashAsync([WriteFile(_usb, name)]));
			Assert.IsTrue(result.Succeeded, result.ErrorMessage);
			return result.Item!;
		}

		private static void PlantEntry(string trashRoot, string name)
		{
			Directory.CreateDirectory(Path.Combine(trashRoot, "files"));
			Directory.CreateDirectory(Path.Combine(trashRoot, "info"));
			File.WriteAllText(Path.Combine(trashRoot, "files", name), "planted");
			File.WriteAllText(Path.Combine(trashRoot, "info", name + ".trashinfo"), $"[Trash Info]\nPath={name}\nDeletionDate=2026-01-01T00:00:00\n");
			File.SetUnixFileMode(trashRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}

		[TestMethod]
		public async Task Restore_TopdirRelativeTraversal_IsRejected()
		{
			var item = await TrashOnUsb();
			TamperInfo(item.TrashId, "../../victim.txt");

			var listed = (await _service.ListAsync()).Single();
			Assert.IsFalse(listed.IsValid);

			var result = Single(await _service.RestoreAsync([listed]));

			Assert.IsFalse(result.Succeeded);
			Assert.AreEqual("Original location outside the volume", result.ErrorMessage);
			Assert.IsFalse(File.Exists(Path.Combine(_root, "victim.txt")));
			Assert.IsTrue(File.Exists(item.TrashedPath));
		}

		[TestMethod]
		public async Task Restore_TopdirAbsolutePathOutsideTopdir_IsRejected()
		{
			var item = await TrashOnUsb();
			var target = Path.Combine(_home, ".bashrc");
			TamperInfo(item.TrashId, target);

			Assert.IsFalse((await _service.ListAsync()).Single().IsValid);
			var result = Single(await _service.RestoreAsync(await _service.ListAsync()));

			Assert.IsFalse(result.Succeeded);
			Assert.IsFalse(File.Exists(target));
		}

		[TestMethod]
		public async Task Restore_TopdirSiblingPrefixPath_IsRejected()
		{
			var item = await TrashOnUsb();
			TamperInfo(item.TrashId, _usb + "-evil/x.txt");

			Assert.IsFalse((await _service.ListAsync()).Single().IsValid);
		}

		[TestMethod]
		public async Task Restore_TopdirAbsolutePathInsideTopdir_IsAccepted()
		{
			var item = await TrashOnUsb();
			var target = Path.Combine(_usb, "sub", "b.txt");
			TamperInfo(item.TrashId, target);

			var listed = (await _service.ListAsync()).Single();
			var result = Single(await _service.RestoreAsync([listed]));

			Assert.IsTrue(listed.IsValid);
			Assert.IsTrue(result.Succeeded, result.ErrorMessage);
			Assert.IsTrue(File.Exists(target));
		}

		[TestMethod]
		public async Task Restore_IgnoresCallerSuppliedOriginalPath()
		{
			var item = await TrashOnUsb();
			var forged = item with { OriginalPath = Path.Combine(_home, ".bashrc") };

			var result = Single(await _service.RestoreAsync([forged]));

			Assert.IsTrue(result.Succeeded, result.ErrorMessage);
			Assert.IsFalse(File.Exists(Path.Combine(_home, ".bashrc")));
			Assert.IsTrue(File.Exists(Path.Combine(_usb, "a.txt")));
		}

		[TestMethod]
		public async Task Restore_ParentSymlinkPointingOutsideVolume_IsRejected()
		{
			var item = await TrashOnUsb();
			var outside = Path.Combine(_home, "outside");
			Directory.CreateDirectory(outside);
			Directory.CreateSymbolicLink(Path.Combine(_usb, "link"), outside);
			TamperInfo(item.TrashId, "link/pwn.txt");

			var result = Single(await _service.RestoreAsync(await _service.ListAsync()));

			Assert.IsFalse(result.Succeeded);
			Assert.IsFalse(File.Exists(Path.Combine(outside, "pwn.txt")));
		}

		[TestMethod]
		public async Task Restore_HomeTrashRelativePath_IsInvalid()
		{
			var item = Single(await _service.TrashAsync([WriteFile(_home, "a.txt")])).Item!;
			TamperInfo(item.TrashId, "relative/a.txt");

			var listed = (await _service.ListAsync()).Single();
			var result = Single(await _service.RestoreAsync([listed]));

			Assert.IsFalse(listed.IsValid);
			Assert.IsFalse(result.Succeeded);
		}

		[TestMethod]
		public async Task Items_OutsideKnownTrashFolders_AreRejected()
		{
			var victim = WriteFile(Path.Combine(_root, "decoy", "files"), "victim.txt");
			var forged = new TrashItem(victim, "/x", DateTimeOffset.Now, 1, false, "id");

			var delete = Single(await _service.DeletePermanentlyAsync([forged]));
			var restore = Single(await _service.RestoreAsync([forged]));

			Assert.IsFalse(delete.Succeeded);
			Assert.IsFalse(restore.Succeeded);
			Assert.IsTrue(File.Exists(victim));
		}

		[TestMethod]
		public async Task Trash_TopdirTrashOwnedByAnotherUser_IsIgnoredAndHomeTrashUsed()
		{
			var planted = Path.Combine(_usb, ".Trash-" + Uid);
			PlantEntry(planted, "evil.txt");
			_inspector.Owners[planted] = 0;

			var result = Single(await _service.TrashAsync([WriteFile(_usb, "a.txt")]));

			Assert.IsTrue(result.Succeeded, result.ErrorMessage);
			StringAssert.StartsWith(result.ResultPath!, HomeTrash);
			Assert.AreEqual("a.txt", (await _service.ListAsync()).Single().Name);
		}

		[TestMethod]
		public async Task List_TopdirTrashWithGroupOrOtherWritableMode_IsNotListed()
		{
			var planted = Path.Combine(_usb, ".Trash-" + Uid);
			PlantEntry(planted, "evil.txt");

			File.SetUnixFileMode(planted, (UnixFileMode)0b111_000_010); // other-writable
			Assert.IsEmpty(await _service.ListAsync());

			File.SetUnixFileMode(planted, (UnixFileMode)0b111_010_000); // group-writable
			Assert.IsEmpty(await _service.ListAsync());

			File.SetUnixFileMode(planted, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			Assert.HasCount(1, await _service.ListAsync());
		}

		[TestMethod]
		public async Task List_TopdirTrashSymlink_IsNotListedAndNotUsed()
		{
			var real = Path.Combine(_root, "elsewhere-trash");
			PlantEntry(real, "evil.txt");
			Directory.CreateSymbolicLink(Path.Combine(_usb, ".Trash-" + Uid), real);

			Assert.IsEmpty(await _service.ListAsync());

			var result = Single(await _service.TrashAsync([WriteFile(_usb, "a.txt")]));
			StringAssert.StartsWith(result.ResultPath!, HomeTrash);
		}

		[TestMethod]
		public async Task List_TopdirFilesSubdirectoryIsSymlink_IsNotListed()
		{
			var planted = Path.Combine(_usb, ".Trash-" + Uid);
			var real = Path.Combine(_root, "elsewhere-files");
			Directory.CreateDirectory(real);
			Directory.CreateDirectory(Path.Combine(planted, "info"));
			File.SetUnixFileMode(planted, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			Directory.CreateSymbolicLink(Path.Combine(planted, "files"), real);
			File.WriteAllText(Path.Combine(real, "evil.txt"), "x");
			File.WriteAllText(Path.Combine(planted, "info", "evil.txt.trashinfo"), "[Trash Info]\nPath=evil.txt\n");

			Assert.IsEmpty(await _service.ListAsync());
		}

		[TestMethod]
		public async Task Trash_SharedTrashUidDirOwnedByAnotherUser_FallsBackToPerUserTrash()
		{
			var shared = Path.Combine(_usb, ".Trash");
			Directory.CreateDirectory(shared);
			File.SetUnixFileMode(shared, (UnixFileMode)0b001_111_111_111);
			var uidDir = Path.Combine(shared, Uid.ToString());
			PlantEntry(uidDir, "evil.txt");
			_inspector.Owners[uidDir] = 0;

			var result = Single(await _service.TrashAsync([WriteFile(_usb, "a.txt")]));

			Assert.IsTrue(result.Succeeded, result.ErrorMessage);
			StringAssert.StartsWith(result.ResultPath!, Path.Combine(_usb, ".Trash-" + Uid));
		}

		[TestMethod]
		public void StatxInspector_ReportsRealOwnerTypeAndMode()
		{
			var inspector = new StatxFileOwnershipInspector();
			var dir = Path.Combine(_root, "d");
			Directory.CreateDirectory(dir);
			File.SetUnixFileMode(dir, (UnixFileMode)0b001_111_101_101);
			var link = Path.Combine(_root, "l");
			Directory.CreateSymbolicLink(link, dir);

			Assert.IsTrue(inspector.TryGetInfo(dir, out var d));
			Assert.IsTrue(d.IsDirectory);
			Assert.IsFalse(d.IsSymbolicLink);
			Assert.AreEqual(new LinuxTrashOptions().UserId, d.OwnerUserId);
			Assert.AreEqual((UnixFileMode)0b001_111_101_101, d.Mode);

			Assert.IsTrue(inspector.TryGetInfo(link, out var l));
			Assert.IsTrue(l.IsSymbolicLink);
			Assert.IsFalse(l.IsDirectory);

			Assert.IsFalse(inspector.TryGetInfo(Path.Combine(_root, "missing"), out _));
		}
	}
}
