// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Files.Platform.Abstractions.FileOperations;
using Files.Platform.Linux.FileOperations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Platform.Tests.FileOperations
{
	[TestClass]
	[SupportedOSPlatform("linux")]
	public sealed class MoveQuarantineTests : FileOperationsTestBase
	{
		[TestMethod]
		[DataRow(false)]
		[DataRow(true)]
		public async Task ChangedAfterVerification_PreservesSource(bool directory)
		{
			var source = directory ? Path.Combine(Src, "tree") : Write(Path.Combine(Src, "a"), "OLD");
			if (directory)
				Write(Path.Combine(source, "child"), "OLD");
			var invoked = false;
			var service = new LinuxFileOperationsService((_, _) => false, new LinuxFileOperationsHooks
			{
				AfterMoveSourceVerified = path =>
				{
					if (path != source)
						return;
					invoked = true;
					Write(directory ? Path.Combine(source, "new") : source, "CHANGED");
				},
			});
			var result = await service.MoveAsync([source], Dst);
			Assert.IsTrue(invoked);
			Assert.AreEqual(FileOperationErrorKind.VerificationFailed, result[0].ErrorKind);
			Assert.AreEqual("CHANGED", File.ReadAllText(directory ? Path.Combine(source, "new") : source));
		}

		[TestMethod]
		public async Task ReplacedBySymlinkDuringRename_RestoresReplacement()
		{
			var source = Write(Path.Combine(Src, "a"), "OLD");
			var precious = Write(Path.Combine(Root, "precious"), "KEEP");
			var swapped = false;
			var service = new LinuxFileOperationsService((_, _) => false, new LinuxFileOperationsHooks
			{
				RenameError = (from, to, _) =>
				{
					if (from == "a" && to.StartsWith(".linuxfiles-move-") && !swapped)
					{
						swapped = true;
						File.Move(source, source + ".saved");
						File.CreateSymbolicLink(source, precious);
					}
					return null;
				},
			});
			var result = await service.MoveAsync([source], Dst);
			Assert.IsTrue(swapped);
			Assert.AreEqual(FileOperationErrorKind.VerificationFailed, result[0].ErrorKind);
			Assert.AreEqual(precious, new FileInfo(source).LinkTarget);
			Assert.AreEqual("KEEP", File.ReadAllText(precious));
			Assert.AreEqual("OLD", File.ReadAllText(source + ".saved"));
			Assert.IsEmpty(Directory.GetFileSystemEntries(Src, ".linuxfiles-move-*"));
		}

		[TestMethod]
		[DataRow(false, false)]
		[DataRow(true, false)]
		[DataRow(false, true)]
		public async Task QuarantineMismatch_RestoresOrRetainsWithoutOverwriting(bool directory, bool occupied)
		{
			var source = directory ? Path.Combine(Src, "tree") : Write(Path.Combine(Src, "a"), "OLD");
			if (directory)
				Write(Path.Combine(source, "child"), "OLD");
			string? quarantined = null;
			var service = new LinuxFileOperationsService((_, _) => false, new LinuxFileOperationsHooks
			{
				MoveSourceQuarantined = (path, hidden) =>
				{
					if (path != source)
						return;
					quarantined = hidden;
					Write(directory ? Path.Combine(hidden, "new") : hidden, "CHANGED");
					if (occupied)
						Write(source, "KEEP");
				},
			});
			var result = await service.MoveAsync([source], Dst);
			Assert.IsNotNull(quarantined);
			Assert.AreEqual(FileOperationErrorKind.VerificationFailed, result[0].ErrorKind);
			var retained = occupied ? quarantined : source;
			Assert.AreEqual("CHANGED", File.ReadAllText(directory ? Path.Combine(retained, "new") : retained));
			if (occupied)
				Assert.AreEqual("KEEP", File.ReadAllText(source));
			else
				Assert.IsEmpty(Directory.GetFileSystemEntries(Src, ".linuxfiles-move-*"));
		}

		[TestMethod]
		public async Task EditedBetweenFinalStatAndRename_RestoresChangedSource()
		{
			var source = Write(Path.Combine(Src, "a"), "OLD");
			var edited = false;
			var service = new LinuxFileOperationsService((_, _) => false, new LinuxFileOperationsHooks
			{
				RenameError = (from, to, _) =>
				{
					if (from == "a" && to.StartsWith(".linuxfiles-move-"))
					{
						edited = true;
						Write(source, "CHANGED AFTER STAT");
					}
					return null;
				},
			});
			var result = await service.MoveAsync([source], Dst);
			Assert.IsTrue(edited);
			Assert.AreEqual(FileOperationErrorKind.VerificationFailed, result[0].ErrorKind);
			Assert.AreEqual("CHANGED AFTER STAT", File.ReadAllText(source));
			Assert.AreEqual("OLD", File.ReadAllText(Path.Combine(Dst, "a")));
			Assert.IsEmpty(Directory.GetFileSystemEntries(Src, ".linuxfiles-move-*"));
		}

		[TestMethod]
		[DataRow(22)]
		[DataRow(38)]
		[DataRow(95)]
		public async Task UnsupportedRename_UsesVerifiedFallback(int errno)
		{
			var source = Write(Path.Combine(Src, "a"), "OLD");
			var service = new LinuxFileOperationsService((_, _) => false, new LinuxFileOperationsHooks
			{
				RenameError = (_, to, _) => to.StartsWith(".linuxfiles-move-") ? errno : null,
			});
			var result = await service.MoveAsync([source], Dst);
			Assert.IsTrue(result[0].Succeeded, result[0].ErrorMessage);
			Assert.IsFalse(File.Exists(source));
			Assert.AreEqual("OLD", File.ReadAllText(Path.Combine(Dst, "a")));
		}
	}
}
