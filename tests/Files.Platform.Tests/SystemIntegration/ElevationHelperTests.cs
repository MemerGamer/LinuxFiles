// Copyright (c) Files Community
// Licensed under the MIT License.

extern alias ElevationHelper;

using Files.Platform.Abstractions.Elevation;

using HelperEngine = ElevationHelper::Files.Platform.Linux.ElevationHelper.HelperEngine;
using Files.Platform.Linux.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace Files.Platform.Tests.SystemIntegration
{
	[System.Runtime.Versioning.SupportedOSPlatform("linux")]
	[TestClass]
	public sealed class ElevationHelperTests
	{
		private string root = null!;
		private string source = null!;
		private string target = null!;

		[TestInitialize]
		public void Setup()
		{
			Assert.AreNotEqual(0U, ProcessIdentityNative.CurrentUserId, "These tests must run without root privileges.");
			root = Path.Combine(Path.GetTempPath(), "files-elevation-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			source = Path.Combine(root, "source"); target = Path.Combine(root, "target");
			Directory.CreateDirectory(source); Directory.CreateDirectory(target);
		}

		[TestCleanup]
		public void Cleanup() => Directory.Delete(root, recursive: true);

		private HelperResponse Run(string operation, string[] sources, string? destination = null, Action<string, string>? hook = null, bool fallback = false)
			=> new HelperEngine(ProcessIdentityNative.CurrentUserId, hook, fallback, RootOwner()).Execute(new(1, operation, sources, destination));

		private static uint RootOwner()
		{
			Assert.IsTrue(new StatxFileOwnershipInspector().TryGetInfo("/", out var info));
			return info.OwnerUserId; // Some CI sandboxes map root-owned directories to uid 65534.
		}

		private string FileAt(string directory, string name, string contents = "confirmed bytes")
		{
			var path = Path.Combine(directory, name); File.WriteAllText(path, contents); return path;
		}

		[TestMethod]
		[DataRow(false)]
		[DataRow(true)]
		public void CopiesAndMovesNestedTreesThroughDescriptors(bool fallback)
		{
			var folder = Directory.CreateDirectory(Path.Combine(source, "tree")).FullName;
			FileAt(folder, "a"); var child = Directory.CreateDirectory(Path.Combine(folder, "sub")).FullName;
			FileAt(child, "empty", "");
			var copied = Run("copy", [folder], target, fallback: fallback);
			Assert.IsTrue(copied.Items.Single().Succeeded, copied.Items.Single().Error);
			Assert.AreEqual("confirmed bytes", File.ReadAllText(Path.Combine(target, "tree", "a")));
			Assert.IsTrue(Directory.Exists(folder));
			// The 0700 test root hides the source from other users, so the copy stays owner-only.
			Assert.AreEqual((UnixFileMode)0x180, File.GetUnixFileMode(Path.Combine(target, "tree", "a")));
			var second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
			var moved = Run("move", [folder], second, fallback: fallback);
			Assert.IsTrue(moved.Items.Single().Succeeded, moved.Items.Single().Error);
			Assert.IsFalse(Directory.Exists(folder));
			Assert.AreEqual("confirmed bytes", File.ReadAllText(Path.Combine(second, "tree", "a")));
		}

		[TestMethod]
		[DataRow(false)]
		[DataRow(true)]
		public void AuthenticationTimeAncestorSymlinkSwapIsRefused(bool fallback)
		{
			var file = FileAt(source, "victim");
			var protectedTree = Directory.CreateDirectory(Path.Combine(root, "protected")).FullName;
			var sentinel = FileAt(protectedTree, "victim", "must survive");
			Directory.Move(source, source + "-old"); Directory.CreateSymbolicLink(source, protectedTree);
			var result = Run("delete", [file], fallback: fallback);
			Assert.IsFalse(result.Items.Single().Succeeded);
			Assert.AreEqual("must survive", File.ReadAllText(sentinel));
			Assert.IsTrue(File.Exists(Path.Combine(source + "-old", "victim")));
		}

		[TestMethod]
		[DataRow(false)]
		[DataRow(true)]
		public void SwappedAncestorAfterPinCannotRedirectDeletion(bool fallback)
		{
			var file = FileAt(source, "victim");
			var protectedTree = Directory.CreateDirectory(Path.Combine(root, "protected")).FullName;
			var sentinel = FileAt(protectedTree, "victim", "must survive");
			var result = Run("delete", [file], hook: (stage, _) =>
			{
				if (stage != "plan-pinned") return;
				Directory.Move(source, source + "-old"); Directory.CreateSymbolicLink(source, protectedTree);
			}, fallback: fallback);
			Assert.IsTrue(result.Items.Single().Succeeded, result.Items.Single().Error);
			Assert.AreEqual("must survive", File.ReadAllText(sentinel));
			Assert.IsFalse(File.Exists(Path.Combine(source + "-old", "victim")));
		}

		[TestMethod]
		public void ComponentAndLeafRacesNeverFollowLinks()
		{
			var file = FileAt(source, "victim"); var sentinel = FileAt(target, "victim", "must survive");
			var swapped = false;
			var result = Run("delete", [file], hook: (stage, name) =>
			{
				if (stage != "before-entry-open" || name != "victim" || swapped) return;
				swapped = true; File.Delete(file); File.CreateSymbolicLink(file, sentinel);
			});
			Assert.IsFalse(result.Items.Single().Succeeded);
			Assert.AreEqual("must survive", File.ReadAllText(sentinel));
		}

		[TestMethod]
		[DataRow("copy")]
		[DataRow("move")]
		public void DestinationAppearingDuringAuthorizationNeverReplacesOrDeletesOriginal(string operation)
		{
			var file = FileAt(source, "a");
			var result = Run(operation, [file], target, (stage, _) =>
			{
				if (stage == "before-copy") FileAt(target, "a", "unrelated");
			});
			Assert.IsFalse(result.Items.Single().Succeeded);
			Assert.AreEqual("unrelated", File.ReadAllText(Path.Combine(target, "a")));
			Assert.AreEqual("confirmed bytes", File.ReadAllText(file));
		}

		[TestMethod]
		public void RenameUsesAtomicNoReplaceEvenForDanglingSymlinkCollision()
		{
			var file = FileAt(source, "a"); var renamed = Path.Combine(source, "b");
			var result = Run("rename", [file], renamed, (stage, _) =>
			{
				if (stage == "before-rename") File.CreateSymbolicLink(renamed, Path.Combine(root, "missing"));
			});
			Assert.IsFalse(result.Items.Single().Succeeded);
			Assert.IsTrue(File.Exists(file));
			File.Delete(renamed);
			Assert.IsTrue(Run("rename", [file], renamed).Items.Single().Succeeded);
			Assert.IsFalse(File.Exists(file));
		}

		[TestMethod]
		public void PartialBatchFailureIsReportedPerItem()
		{
			var first = FileAt(source, "a"); var second = FileAt(source, "b"); FileAt(target, "b", "collision");
			var result = Run("move", [first, second], target);
			Assert.IsTrue(result.Items[0].Succeeded, result.Items[0].Error);
			Assert.IsFalse(result.Items[1].Succeeded);
			Assert.IsFalse(File.Exists(first)); Assert.IsTrue(File.Exists(second));
			Assert.AreEqual("collision", File.ReadAllText(Path.Combine(target, "b")));
		}

		[TestMethod]
		public void CopyCorruptionAndSourceChangePreventMoveDeletion()
		{
			var file = FileAt(source, "a");
			var result = Run("move", [file], target, (stage, _) =>
			{
				if (stage == "after-copy") File.WriteAllText(Path.Combine(target, "a"), "corruption");
			});
			Assert.IsFalse(result.Items.Single().Succeeded); Assert.IsTrue(File.Exists(file));
			File.Delete(Path.Combine(target, "a"));
			result = Run("move", [file], target, (stage, _) =>
			{
				if (stage == "before-delete") File.WriteAllText(file, "modified original");
			});
			Assert.IsFalse(result.Items.Single().Succeeded);
			Assert.AreEqual("modified original", File.ReadAllText(file));
		}

		[TestMethod]
		public void RecursiveFailureRetainsOriginalsAndDoesNotCleanUpUnrelatedEntries()
		{
			var folder = Directory.CreateDirectory(Path.Combine(source, "tree")).FullName;
			FileAt(folder, "a"); FileAt(folder, "b");
			var creations = 0;
			var result = Run("move", [folder], target, (stage, name) =>
			{
				if (stage == "before-output-create" && name != "tree" && ++creations == 2)
					throw new IOException("Simulated disk write failure.");
			});
			Assert.IsFalse(result.Items.Single().Succeeded);
			Assert.IsTrue(File.Exists(Path.Combine(folder, "a"))); Assert.IsTrue(File.Exists(Path.Combine(folder, "b")));
			Assert.AreEqual(1, Directory.GetFiles(Path.Combine(target, "tree")).Length, "A partial output is reported and retained.");
		}

		[TestMethod]
		public void RejectsSymlinkTreesAndSetIdCopiesBeforeMutation()
		{
			var folder = Directory.CreateDirectory(Path.Combine(source, "tree")).FullName;
			var sentinel = FileAt(target, "sentinel"); File.CreateSymbolicLink(Path.Combine(folder, "link"), sentinel);
			Assert.IsFalse(Run("delete", [folder]).Items.Single().Succeeded);
			Assert.AreEqual("confirmed bytes", File.ReadAllText(sentinel));
			var file = FileAt(source, "setid"); File.SetUnixFileMode(file, File.GetUnixFileMode(file) | UnixFileMode.SetUser);
			Assert.IsFalse(Run("copy", [file], target).Items.Single().Succeeded);
			Assert.IsFalse(File.Exists(Path.Combine(target, "setid")));
		}

		[TestMethod]
		public void DestinationAncestorSwapAfterPinStaysInOriginalFolder()
		{
			var file = FileAt(source, "a"); var protectedTree = Directory.CreateDirectory(Path.Combine(root, "protected")).FullName;
			var result = Run("copy", [file], target, (stage, _) =>
			{
				if (stage != "plan-pinned") return;
				Directory.Move(target, target + "-old"); Directory.CreateSymbolicLink(target, protectedTree);
			});
			Assert.IsTrue(result.Items.Single().Succeeded, result.Items.Single().Error);
			Assert.IsFalse(File.Exists(Path.Combine(protectedTree, "a")));
			Assert.AreEqual("confirmed bytes", File.ReadAllText(Path.Combine(target + "-old", "a")));
		}

		[TestMethod]
		public void ProtectedSourcesAreRefusedBeforeFilesystemAccess()
		{
			foreach (var path in new[] { "/", "/usr", "/etc", "/boot", "/bin", "/lib", "/lib32", "/lib64", "/libx32", "/sbin",
				"/var", "/home", "/root", "/proc", "/sys", "/dev", "/run", "/srv", "/opt", "/mnt", "/media", "/tmp" })
				foreach (var operation in new[] { "delete", "move", "rename" })
					Assert.ThrowsExactly<InvalidDataException>(() => Run(operation, [path], operation == "delete" ? null :
						operation == "rename" ? path + "-renamed" : target,
						(_, _) => Assert.Fail("A protected plan reached filesystem access.")), path + " " + operation);
		}

		[TestMethod]
		public void VirtualFilesystemDestinationsAreRefusedBeforeOpeningSources()
		{
			foreach (var path in new[] { "/proc", "/proc/1", "/sys", "/sys/kernel", "/dev", "/dev/shm" })
				foreach (var operation in new[] { "copy", "move" })
					Assert.ThrowsExactly<InvalidDataException>(() => Run(operation, [Path.Combine(source, "a")], path,
						(_, _) => Assert.Fail("A virtual destination reached filesystem access.")));
			ElevationHelperProtocol.Validate(new(1, "copy", ["/tmp/a"], "/device"));
		}

		[TestMethod]
		[DataRow("copy", false)]
		[DataRow("move", true)]
		public void CallerOwnedDestinationGetsUsableVerifiedCopies(string operation, bool fallback)
		{
			var folder = Directory.CreateDirectory(Path.Combine(source, "tree")).FullName;
			var file = FileAt(folder, "read-only");
			File.SetUnixFileMode(file, (UnixFileMode)0x100);
			var stages = 0;
			var result = Run(operation, [folder], target, (stage, name) =>
			{
				if (stage != "before-output-ownership") return;
				stages++;
				if (name == "read-only") Assert.AreEqual((UnixFileMode)0x180, File.GetUnixFileMode(Path.Combine(target, "tree", name)));
			}, fallback);
			Assert.IsTrue(result.Items.Single().Succeeded, result.Items.Single().Error);
			Assert.AreEqual(2, stages);
			Assert.AreEqual((UnixFileMode)0x100, File.GetUnixFileMode(Path.Combine(target, "tree", "read-only")));
			Assert.AreEqual((UnixFileMode)0x1C0, File.GetUnixFileMode(Path.Combine(target, "tree")));
			Assert.IsTrue(new StatxFileOwnershipInspector().TryGetInfo(Path.Combine(target, "tree", "read-only"), out var info));
			Assert.AreEqual(ProcessIdentityNative.CurrentUserId, info.OwnerUserId);
			Assert.AreEqual("confirmed bytes", File.ReadAllText(Path.Combine(target, "tree", "read-only")));
		}

		[TestMethod]
		public void RootOwnedDestinationKeepsPrivateOutputModes()
		{
			var destination = Path.GetTempPath().TrimEnd('/');
			Assert.IsTrue(new StatxFileOwnershipInspector().TryGetInfo(destination, out var info));
			if (info.OwnerUserId == ProcessIdentityNative.CurrentUserId) Assert.Inconclusive("Requires root-owned temporary directory.");
			var name = "files-private-copy-" + Guid.NewGuid().ToString("N");
			var folder = Directory.CreateDirectory(Path.Combine(source, name)).FullName;
			FileAt(folder, "a");
			var output = Path.Combine(destination, name);
			try
			{
				var result = Run("copy", [folder], destination);
				Assert.IsTrue(result.Items.Single().Succeeded, result.Items.Single().Error);
				Assert.AreEqual((UnixFileMode)0x1C0, File.GetUnixFileMode(output));
				Assert.AreEqual((UnixFileMode)0x180, File.GetUnixFileMode(Path.Combine(output, "a")));
			}
			finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
		}

		[TestMethod]
		public void OwnershipIsAppliedToVerifiedDescriptorDespiteLeafReplacement()
		{
			var file = FileAt(source, "a");
			var output = Path.Combine(target, "a");
			var displaced = Path.Combine(target, "pinned");
			var result = Run("copy", [file], target, (stage, _) =>
			{
				if (stage != "before-output-ownership") return;
				File.Move(output, displaced);
				FileAt(target, "a", "unrelated");
				File.SetUnixFileMode(output, (UnixFileMode)0x180);
			});
			Assert.IsFalse(result.Items.Single().Succeeded);
			Assert.AreEqual("unrelated", File.ReadAllText(output));
			Assert.AreEqual((UnixFileMode)0x180, File.GetUnixFileMode(output));
			Assert.AreEqual((UnixFileMode)0x180, File.GetUnixFileMode(displaced));
		}

		[TestMethod]
		public void HandoverNeverWidensSourcePermissions()
		{
			// A shared parent lets other users reach the source, so same-group group/other bits may survive.
			var shared = Path.Combine(Path.GetTempPath(), "files-elevation-shared-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(shared, (UnixFileMode)0x1ED);
			try
			{
				File.SetUnixFileMode(shared, (UnixFileMode)0x1ED);
				var origin = Directory.CreateDirectory(Path.Combine(shared, "origin")).FullName;
				File.SetUnixFileMode(origin, (UnixFileMode)0x1ED);
				var hidden = Directory.CreateDirectory(Path.Combine(origin, "hidden")).FullName;
				File.SetUnixFileMode(hidden, (UnixFileMode)0x1C0);
				var key = FileAt(hidden, "key"); File.SetUnixFileMode(key, (UnixFileMode)0x1A4);
				var team = FileAt(origin, "team"); File.SetUnixFileMode(team, (UnixFileMode)0x1A0);
				var result = Run("copy", [hidden, team], target);
				Assert.IsTrue(result.Items.All(item => item.Succeeded), string.Join("; ", result.Items.Select(item => item.Error)));
				Assert.AreEqual((UnixFileMode)0x1C0, File.GetUnixFileMode(Path.Combine(target, "hidden")), "A 0700 folder must not become 0755.");
				Assert.AreEqual((UnixFileMode)0x1A4, File.GetUnixFileMode(Path.Combine(target, "hidden", "key")));
				Assert.AreEqual((UnixFileMode)0x1A0, File.GetUnixFileMode(Path.Combine(target, "team")));
			}
			finally { Directory.Delete(shared, true); }
		}

		[TestMethod]
		[DataRow(0x1C0U, true, true, true, false, 0x1C0U)] // private folder stays private
		[DataRow(0x3FFU, true, true, true, false, 0x1EDU)] // sticky and group/other write are dropped
		[DataRow(0x1A0U, false, false, true, false, 0x180U)] // 0640 root:shadow never reaches the caller's group
		[DataRow(0x1A4U, false, false, true, false, 0x184U)] // others keep read; the foreign group's read is not transferred
		[DataRow(0x1A4U, false, true, false, false, 0x180U)] // the source lived where others could not reach it
		[DataRow(0x1A4U, false, true, true, true, 0x180U)] // ACL masks are not real group grants
		[DataRow(0x1C4U, false, false, true, false, 0x180U)] // others never gain what the source group lacked
		[DataRow(0x1A4U, false, true, true, false, 0x1A4U)]
		[DataRow(0x9EDU, false, true, true, false, 0x1A4U)]
		public void HandoverModeMasksEveryClass(uint source, bool directory, bool sameGroup, bool othersReach, bool acl, uint expected)
			=> Assert.AreEqual(expected, HelperEngine.HandoverMode(source | (directory ? 0x4000U : 0x8000U), directory, sameGroup, othersReach, acl));

		[TestMethod]
		public void HardLinkedFilesAreNotHandedToCaller()
		{
			var folder = Directory.CreateDirectory(Path.Combine(source, "tree")).FullName;
			var file = FileAt(folder, "a");
			using (var link = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/ln", ["--", file, Path.Combine(root, "alias")]) { UseShellExecute = false })!)
			{
				link.WaitForExit();
				Assert.AreEqual(0, link.ExitCode);
			}
			var result = Run("move", [folder], target);
			Assert.IsFalse(result.Items.Single().Succeeded);
			Assert.IsFalse(Directory.Exists(Path.Combine(target, "tree")), "The refusal happens before any output is created.");
			Assert.IsTrue(File.Exists(file));
		}

		[TestMethod]
		public void ErrorsDoNotNameNestedEntries()
		{
			var folder = Directory.CreateDirectory(Path.Combine(source, "tree")).FullName;
			FileAt(folder, "nested-secret-name");
			var result = Run("copy", [folder], target, (stage, name) =>
			{
				if (stage == "before-output-create" && name == "nested-secret-name") FileAt(Path.Combine(target, "tree"), name, "collision");
			});
			Assert.IsFalse(result.Items.Single().Succeeded);
			StringAssert.Contains(result.Items.Single().Error, "errno 17");
			Assert.IsFalse(result.Items.Single().Error.Contains("nested-secret-name"), result.Items.Single().Error);
			result = Run("delete", [folder], hook: (stage, name) =>
			{
				if (stage == "before-entry-open" && name == "nested-secret-name") throw new UnauthorizedAccessException($"Access to '{name}' is denied.");
			});
			Assert.AreEqual("Access is denied.", result.Items.Single().Error);
		}

		[TestMethod]
		public void StrictProtocolRejectsMalformedForgedAndOverlappingPlans()
		{
			var valid = ElevationHelperProtocol.Serialize(new HelperRequest(1, "delete", [Path.Combine(source, "a")], null));
			Assert.AreEqual("delete", ElevationHelperProtocol.ParseRequest(valid).Operation);
			foreach (var json in new[] { valid.Replace("\"version\":1", "\"version\":1,\"version\":1"), valid.Replace("\"version\":1", "\"version\":2"),
				valid.Replace("\"version\":1", "\"extra\":1,\"version\":1"), valid.Replace("delete", "shell"), valid.Replace(",\"target\":null", ""), valid + "{}" })
				Assert.Throws<Exception>(() => ElevationHelperProtocol.ParseRequest(json));
			foreach (var path in new[] { "/", "relative", "/tmp/../etc", "/tmp//a", "/tmp/a/", "/tmp/a\0b" })
				Assert.ThrowsExactly<InvalidDataException>(() => ElevationHelperProtocol.ValidatePath(path));
			Assert.ThrowsExactly<InvalidDataException>(() => Run("delete", [source, Path.Combine(source, "a")]));
		}
	}
}
