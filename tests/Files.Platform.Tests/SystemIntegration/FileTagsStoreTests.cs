// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Tags;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.IO;

namespace Files.Platform.Tests.SystemIntegration
{
	[TestClass]
	public sealed class FileTagsStoreTests
	{
		private static (string Dir, string File, XattrFileTagsStore Store) Create()
		{
			var dir = Path.Combine(AppContext.BaseDirectory, "tags-" + Guid.NewGuid().ToString("N")[..8]);
			Directory.CreateDirectory(dir);
			var file = Path.Combine(dir, "a.txt");
			File.WriteAllText(file, "x");
			return (dir, file, new XattrFileTagsStore());
		}

		[TestMethod]
		public void RoundTripsTagsOrSkipsWithoutXattrSupport()
		{
			var (dir, file, store) = Create();
			try
			{
				if (!store.WriteTags(file, ["Work", "Per sonal", "ünï"]))
					Assert.Inconclusive("The test file system has no user xattr support.");

				CollectionAssert.AreEqual(new[] { "Work", "Per sonal", "ünï" }, store.ReadTags(file).ToArray());

				Assert.IsTrue(store.WriteTags(file, []));
				Assert.AreEqual(0, store.ReadTags(file).Count);
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}

		[TestMethod]
		public void CleansCommasAndDuplicates()
		{
			var (dir, file, store) = Create();
			try
			{
				if (!store.WriteTags(file, ["a", "b,c", "a"]))
					Assert.Inconclusive("The test file system has no user xattr support.");

				CollectionAssert.AreEqual(new[] { "a", "b c" }, store.ReadTags(file).ToArray());
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}

		[TestMethod]
		public void MissingFileOrNoTagsDegradesGracefully()
		{
			var store = new XattrFileTagsStore();
			Assert.AreEqual(0, store.ReadTags("/nonexistent/file").Count);
			Assert.IsFalse(store.WriteTags("/nonexistent/file", ["x"]));
			Assert.AreEqual(0, store.ReadTags("").Count);
		}
	}
}
