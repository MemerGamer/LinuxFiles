// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils;
using Files.App.Utils.FileTags;
using Files.Platform.Abstractions.Tags;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Tags
{
	[TestClass]
	[DoNotParallelize]
	public sealed class FileTagsHelperTests
	{
		[TestMethod]
		public async Task Drain_WaitsForAllQueuedEditsAndPersistsThem()
		{
			using var fixture = new TagFixture();
			_ = FileTagsHelper.WriteFileTagAsync("/blocked", ["a"]);
			await fixture.Store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
			for (var i = 0; i < 12; i++)
				_ = FileTagsHelper.WriteFileTagAsync("/file" + i, ["b"]);
			var drain = FileTagsHelper.DrainPendingWritesAsync();
			Assert.IsFalse(drain.IsCompleted);
			fixture.Store.Release.Set();
			await drain.WaitAsync(TimeSpan.FromSeconds(10));
			var reloaded = new FileTagsDatabase(fixture.DatabasePath);
			CollectionAssert.AreEqual(new[] { "a" }, reloaded.GetTags("/blocked", null));
			for (var i = 0; i < 12; i++)
			{
				CollectionAssert.AreEqual(new[] { "b" }, reloaded.GetTags("/file" + i, null));
				CollectionAssert.AreEqual(new[] { "B" }, fixture.Store.Tags["/file" + i]);
			}
		}

		[TestMethod]
		public async Task BulkDeletion_PreservesQueuedAdditionsAndIncludesNewlyTaggedFiles()
		{
			using var fixture = new TagFixture();
			FileTagsHelper.GetDbInstance().SetTags("/existing", null, ["a"]);
			_ = FileTagsHelper.WriteFileTagAsync("/blocked", ["a"]);
			await fixture.Store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
			_ = FileTagsHelper.WriteFileTagAsync("/existing", ["a", "b"]);
			_ = FileTagsHelper.WriteFileTagAsync("/new", ["a", "b"]);
			fixture.Settings.FileTagList = [new("b", "B")];
			var deletion = FileTagsHelper.UntagAllFilesAsync("a");
			Assert.IsFalse(deletion.IsCompleted);
			fixture.Store.Release.Set();
			Assert.IsTrue(await deletion.WaitAsync(TimeSpan.FromSeconds(10)));
			await FileTagsHelper.DrainPendingWritesAsync();
			var reloaded = new FileTagsDatabase(fixture.DatabasePath);
			foreach (var path in new[] { "/existing", "/new" })
			{
				CollectionAssert.AreEqual(new[] { "b" }, reloaded.GetTags(path, null));
				CollectionAssert.AreEqual(new[] { "B" }, fixture.Store.Tags[path]);
			}
			Assert.AreEqual(0, reloaded.GetTags("/blocked", null).Length);
			Assert.AreEqual(0, fixture.Store.Tags["/blocked"].Length);
		}

		[TestMethod]
		[DataRow(true)]
		[DataRow(false)]
		public async Task BulkDeletion_SerializesPropertyLoadingReadAndDatabaseUpdate(bool supportsXattrs)
		{
			using var fixture = new TagFixture();
			fixture.Store.SupportsXattrs = supportsXattrs;
			fixture.Store.BlockReads = true;
			fixture.Store.Tags["/existing"] = ["A", "B"];
			if (!supportsXattrs)
				FileTagsHelper.GetDbInstance().SetTags("/existing", null, ["a", "b"]);

			var loading = FileTagsHelper.ReadAndUpdateFileTagsAsync("/existing");
			await fixture.Store.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
			var deletion = FileTagsHelper.UntagAllFilesAsync("a");
			var reloading = FileTagsHelper.ReadAndUpdateFileTagsAsync("/existing");
			Assert.IsFalse(deletion.IsCompleted);
			Assert.IsFalse(reloading.IsCompleted);
			fixture.Store.ReadRelease.Set();
			CollectionAssert.AreEqual(new[] { "a", "b" }, await loading.WaitAsync(TimeSpan.FromSeconds(10)));
			Assert.IsTrue(await deletion.WaitAsync(TimeSpan.FromSeconds(10)));

			CollectionAssert.AreEqual(new[] { "b" }, await reloading.WaitAsync(TimeSpan.FromSeconds(10)));
			var reloaded = new FileTagsDatabase(fixture.DatabasePath);
			CollectionAssert.AreEqual(new[] { "b" }, reloaded.GetTags("/existing", null));
		}

		[TestMethod]
		[DataRow(true, false)]
		[DataRow(false, false)]
		[DataRow(true, true)]
		[DataRow(false, true)]
		public async Task EditsAcrossItems_MergeInsideQueueWithoutDroppingPendingTags(bool supportsXattrs, bool secondItemLoaded)
		{
			using var fixture = new TagFixture();
			fixture.Store.SupportsXattrs = supportsXattrs;
			fixture.Store.Tags["/preset"] = ["A"];
			FileTagsHelper.GetDbInstance().SetTags("/preset", null, ["a"]);
			_ = FileTagsHelper.WriteFileTagAsync("/blocked", ["a"]);
			await fixture.Store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

			var firstItem = new ListedItem { ItemPath = "/preset" };
			firstItem.SetLoadedFileTags(["a"]);
			var secondItem = new ListedItem { ItemPath = "/preset" };
			if (secondItemLoaded)
				secondItem.SetLoadedFileTags(["a"]);
			var firstEdit = firstItem.EditFileTagsAsync(tags => [.. tags, "b"]);
			var secondEdit = secondItem.EditFileTagsAsync(tags => [.. tags, "c"]);
			Assert.IsFalse(firstEdit.IsCompleted);
			Assert.IsFalse(secondEdit.IsCompleted);

			fixture.Store.Release.Set();
			await Task.WhenAll(firstEdit, secondEdit).WaitAsync(TimeSpan.FromSeconds(10));
			CollectionAssert.AreEqual(new[] { "a", "b", "c" }, secondItem.FileTags);
			CollectionAssert.AreEqual(new[] { "a", "b", "c" }, new FileTagsDatabase(fixture.DatabasePath).GetTags("/preset", null));
			if (supportsXattrs)
				CollectionAssert.AreEqual(new[] { "A", "B", "C" }, fixture.Store.Tags["/preset"]);
		}

		[TestMethod]
		public async Task DeferredLoad_DiscardedAfterEditBeforeCallback_DoesNotDropTagsOnNextEdit()
		{
			using var fixture = new TagFixture();
			fixture.Store.Tags["/preset"] = ["A"];
			var item = new ListedItem { ItemPath = "/preset" };
			var revision = item.FileTagsRevision;
			var snapshot = await FileTagsHelper.ReadAndUpdateFileTagsAsync("/preset");

			await item.EditFileTagsAsync(tags => [.. tags, "b"]);
			item.SetLoadedFileTags(snapshot, revision);
			CollectionAssert.AreEqual(new[] { "a", "b" }, item.FileTags);
			Assert.IsTrue(item.HasTags);
			await item.EditFileTagsAsync(tags => [.. tags, "c"]);

			CollectionAssert.AreEqual(new[] { "A", "B", "C" }, fixture.Store.Tags["/preset"]);
			CollectionAssert.AreEqual(new[] { "a", "b", "c" }, new FileTagsDatabase(fixture.DatabasePath).GetTags("/preset", null));
		}

		[TestMethod]
		public async Task DeferredLoad_WhileEditIsPending_IsDiscardedAndNewerEditWins()
		{
			using var fixture = new TagFixture();
			fixture.Store.Tags["/preset"] = ["A"];
			var item = new ListedItem { ItemPath = "/preset" };
			var revision = item.FileTagsRevision;
			var snapshot = await FileTagsHelper.ReadAndUpdateFileTagsAsync("/preset");
			_ = FileTagsHelper.WriteFileTagAsync("/blocked", ["a"]);
			await fixture.Store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

			var firstEdit = item.EditFileTagsAsync(tags => [.. tags, "b"]);
			item.SetLoadedFileTags(snapshot, revision);
			Assert.IsNull(item.FileTags);
			var secondEdit = item.EditFileTagsAsync(tags => tags.Where(uid => uid != "a").Append("c").ToArray());
			fixture.Store.Release.Set();
			await Task.WhenAll(firstEdit, secondEdit).WaitAsync(TimeSpan.FromSeconds(10));

			CollectionAssert.AreEqual(new[] { "b", "c" }, item.FileTags);
			CollectionAssert.AreEqual(new[] { "B", "C" }, fixture.Store.Tags["/preset"]);
		}

		[TestMethod]
		public async Task ExplicitReplacement_InvalidatesDeferredLoad()
		{
			using var fixture = new TagFixture();
			var item = new ListedItem { ItemPath = "/preset" };
			var revision = item.FileTagsRevision;
			item.FileTags = ["b"];
			item.SetLoadedFileTags(["a"], revision);
			await FileTagsHelper.DrainPendingWritesAsync();

			CollectionAssert.AreEqual(new[] { "b" }, item.FileTags);
			CollectionAssert.AreEqual(new[] { "B" }, fixture.Store.Tags["/preset"]);
		}

		[TestMethod]
		public void DeferredLoad_WithUnchangedRevision_UpdatesItemWithoutWriting()
		{
			using var fixture = new TagFixture();
			var item = new ListedItem { ItemPath = "/preset" };
			item.SetLoadedFileTags(["a"], item.FileTagsRevision);

			CollectionAssert.AreEqual(new[] { "a" }, item.FileTags);
			Assert.IsTrue(item.HasTags);
			Assert.AreEqual(0, fixture.Store.Tags.Count);
			Assert.AreEqual(0, FileTagsHelper.GetDbInstance().GetTags("/preset", null).Length);
		}

		[TestMethod]
		public async Task EditOfUntaggedItem_ReachesXattrAndSurvivesRelaunch()
		{
			using var fixture = new TagFixture();
			Assert.AreEqual(0, FileTagsHelper.ReadFileTag("/fresh").Length);

			_ = FileTagsHelper.WriteFileTagAsync("/fresh", ["b"]);
			await FileTagsHelper.DrainPendingWritesAsync();

			// A relaunch reads the tags back from the attribute, then falls back to the database when the attribute is gone
			CollectionAssert.AreEqual(new[] { "b" }, FileTagsHelper.ReadFileTag("/fresh"));
			fixture.Store.Tags.Remove("/fresh");
			CollectionAssert.AreEqual(new[] { "b" }, FileTagsHelper.ReadFileTag("/fresh"));
		}

		private sealed class TagFixture : IDisposable
		{
			private readonly string root = Path.Combine(Path.GetTempPath(), "files-tags-queue-" + Guid.NewGuid().ToString("N"));
			private readonly ServiceProvider services;
			public string DatabasePath => Path.Combine(root, "tags.json");
			public BlockingTagStore Store { get; } = new();
			public TagSettings Settings { get; } = new();

			public TagFixture()
			{
				Directory.CreateDirectory(root);
				FileTagsHelper.Database = new FileTagsDatabase(DatabasePath);
				services = new ServiceCollection().AddSingleton<IFileTagsStore>(Store)
					.AddSingleton<IFileTagsSettingsService>(Settings).BuildServiceProvider();
				Ioc.Default.Services = services;
			}

			public void Dispose()
			{
				Store.Release.Set();
				Store.ReadRelease.Set();
				FileTagsHelper.DrainPendingWritesAsync().GetAwaiter().GetResult();
				Ioc.Default.Services = null;
				services.Dispose();
				Store.Release.Dispose();
				Store.ReadRelease.Dispose();
				Directory.Delete(root, true);
			}
		}

		private sealed class BlockingTagStore : IFileTagsStore
		{
			public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
			public ManualResetEventSlim Release { get; } = new();
			public Dictionary<string, string[]> Tags { get; } = new();
			public bool SupportsXattrs { get; set; } = true;
			public bool BlockReads { get; set; }
			public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
			public ManualResetEventSlim ReadRelease { get; } = new();
			public IReadOnlyList<string> ReadTags(string path)
			{
				if (BlockReads && !ReadEntered.Task.IsCompleted)
				{
					ReadEntered.SetResult();
					if (!ReadRelease.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
				}
				return SupportsXattrs ? Tags.GetValueOrDefault(path) ?? [] : [];
			}
			public bool WriteTags(string path, IReadOnlyList<string> tags)
			{
				if (path == "/blocked" && !Entered.Task.IsCompleted)
				{
					Entered.SetResult();
					if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
				}
				if (!SupportsXattrs) return false;
				Tags[path] = tags.ToArray();
				return true;
			}
		}

		private sealed class TagSettings : IFileTagsSettingsService
		{
			public IList<TestTag> FileTagList { get; set; } = [new("a", "A"), new("b", "B"), new("c", "C")];
			public IList<TestTag>? GetTagsByIds(string[] tags) => FileTagList.Where(tag => tags.Contains(tag.Uid)).ToList();
			public IEnumerable<TestTag> GetTagsByName(string name) => FileTagList.Where(tag => tag.Name == name);
		}
	}
}

namespace Files.App.Utils.FileTags
{
	public static partial class FileTagsHelper
	{
		internal static FileTagsDatabase Database { get; set; } = null!;
		public static FileTagsDatabase GetDbInstance() => Database;
	}

	internal sealed record TestTag(string Uid, string Name);

	internal interface IFileTagsSettingsService
	{
		IList<TestTag> FileTagList { get; }
		IList<TestTag>? GetTagsByIds(string[] tags);
		IEnumerable<TestTag> GetTagsByName(string name);
	}

	internal static class LogPathHelper
	{
		public static string RedactPath(string path) => path;
	}
}

// The linked item partial uses these UI-independent stand-ins for its other members.
namespace Files.App.Utils
{
	public partial class ListedItem
	{
		public string ItemPath { get; set; } = string.Empty;
		public string GetRequiredPath() => ItemPath;
		public bool HasTags { get; private set; }
		public object? FileTagsUI => null;

		private bool SetProperty<T>(ref T field, T value, string? propertyName = null)
		{
			if (EqualityComparer<T>.Default.Equals(field, value)) return false;
			field = value;
			return true;
		}

		private void OnPropertyChanged(string propertyName) { }
	}
}
