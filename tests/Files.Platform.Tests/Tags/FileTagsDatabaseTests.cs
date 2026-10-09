// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.FileTags;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace Files.Platform.Tests.Tags
{
	[TestClass]
	public sealed class FileTagsDatabaseTests
	{
		[TestMethod]
		public void Save_SkipsEmptyAndEqualTagsAndKeepsInputAndOutputIsolated()
		{
			var root = Path.Combine(Path.GetTempPath(), "files-tags-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				var path = Path.Combine(root, "tags.json");
				var db = new FileTagsDatabase(path);
				db.SetTags("/file", null, []);
				Assert.IsFalse(File.Exists(path));
				var tags = new[] { "a", "b" };
				db.SetTags("/file", null, tags);
				tags[0] = "changed";
				var timestamp = DateTime.UtcNow.AddDays(-1);
				File.SetLastWriteTimeUtc(path, timestamp);
				var savedTimestamp = File.GetLastWriteTimeUtc(path);
				db.SetTags("/file", null, ["a", "b"]);
				db.Import(db.Export());
				db.UpdateTag("/file", null, "/file");
				Assert.AreEqual(savedTimestamp, File.GetLastWriteTimeUtc(path));
				var read = db.GetTags("/file", null);
				read[0] = "mutated";
				CollectionAssert.AreEqual(new[] { "a", "b" }, db.GetTags("/file", null));
				db.SetTags("/file", null, ["b", "a"]);
				Assert.AreNotEqual(savedTimestamp, File.GetLastWriteTimeUtc(path));
				db.UpdateTag("/file", null, "/renamed");
				Assert.AreEqual(0, db.GetTags("/file", null).Length);
				CollectionAssert.AreEqual(new[] { "b", "a" }, new FileTagsDatabase(path).GetTags("/renamed", null));
				db.SetTags("/renamed", null, []);
				Assert.AreEqual(0, new FileTagsDatabase(path).GetAll().Count());
				Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp").Length);
			}
			finally { Directory.Delete(root, true); }
		}

		[TestMethod]
		public void Save_RetriesFailedPersistenceEvenWhenTagsAreEqual()
		{
			var root = Path.Combine(Path.GetTempPath(), "files-tags-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				var path = Path.Combine(root, "tags.json");
				Directory.CreateDirectory(path);
				var db = new FileTagsDatabase(path);
				db.SetTags("/file", null, ["a"]);
				Directory.Delete(path);
				db.SetTags("/file", null, ["a"]);
				CollectionAssert.AreEqual(new[] { "a" }, new FileTagsDatabase(path).GetTags("/file", null));
			}
			finally { Directory.Delete(root, true); }
		}
	}
}

// App-only services are never resolved when the database has an explicit test path.
namespace Files.App.Utils.FileTags
{
	[System.Text.Json.Serialization.JsonSerializable(typeof(TaggedFile[]))]
	[System.Text.Json.Serialization.JsonSerializable(typeof(System.Collections.Generic.List<TaggedFile>))]
	internal partial class AppJsonSerializerContext : System.Text.Json.Serialization.JsonSerializerContext { }

	internal sealed class RegistrySerializableAttribute : Attribute { }

	internal sealed class Ioc
	{
		public static Ioc Default { get; } = new();
		public T GetRequiredService<T>() => throw new InvalidOperationException("Tests must use an explicit database path.");
	}

	internal static class App
	{
		public static Microsoft.Extensions.Logging.ILogger? Logger => null;
	}

	internal static class SafetyExtensions
	{
		public static void IgnoreExceptions(Action action)
		{
			try { action(); } catch (IOException) { }
		}
	}
}
