// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Recent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace Files.Platform.Tests.SystemIntegration
{
	[TestClass]
	public sealed class RecentFilesStoreTests
	{
		private static string NewDir()
		{
			var dir = Path.Combine(AppContext.BaseDirectory, "recent-" + Guid.NewGuid().ToString("N")[..8]);
			Directory.CreateDirectory(dir);
			return dir;
		}

		[TestMethod]
		public void AddReadRemoveClear()
		{
			var dir = NewDir();
			try
			{
				using var store = new XbelRecentFilesStore(dir);
				Assert.AreEqual(0, store.Read().Count);

				Assert.IsTrue(store.Add("/tmp/a b#c.txt", "text/plain"));
				System.Threading.Thread.Sleep(5);
				Assert.IsTrue(store.Add("/tmp/second.png"));

				var entries = store.Read();
				Assert.AreEqual(2, entries.Count);
				Assert.AreEqual("/tmp/second.png", entries[0].Path);
				Assert.AreEqual("/tmp/a b#c.txt", entries[1].Path);
				Assert.AreEqual("text/plain", entries[1].MimeType);

				var xml = File.ReadAllText(Path.Combine(dir, "recently-used.xbel"));
				StringAssert.Contains(xml, "file:///tmp/a%20b%23c.txt");
				StringAssert.Contains(xml, "<bookmark:application name=\"Files\"");

				Assert.IsTrue(store.Remove("/tmp/second.png"));
				Assert.AreEqual(1, store.Read().Count);

				Assert.IsTrue(store.Clear());
				Assert.AreEqual(0, store.Read().Count);
				Assert.AreEqual(0, Directory.GetFiles(dir, "*.tmp").Length);
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}

		[TestMethod]
		public void ReAddingRefreshesInsteadOfDuplicating()
		{
			var dir = NewDir();
			try
			{
				using var store = new XbelRecentFilesStore(dir);
				store.Add("/tmp/a.txt");
				System.Threading.Thread.Sleep(5);
				store.Add("/tmp/b.txt");
				System.Threading.Thread.Sleep(5);
				store.Add("/tmp/a.txt");

				var entries = store.Read();
				Assert.AreEqual(2, entries.Count);
				Assert.AreEqual("/tmp/a.txt", entries[0].Path);
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}

		[TestMethod]
		public void CapsEntriesAndKeepsNewest()
		{
			var dir = NewDir();
			try
			{
				using var store = new XbelRecentFilesStore(dir, maxEntries: 3);
				for (var i = 0; i < 6; i++)
				{
					store.Add($"/tmp/f{i}.txt");
					System.Threading.Thread.Sleep(5);
				}

				CollectionAssert.AreEqual(new[] { "/tmp/f5.txt", "/tmp/f4.txt", "/tmp/f3.txt" }, store.Read().Select(e => e.Path).ToArray());
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}

		[TestMethod]
		public void PreservesEntriesFromOtherApplicationsAndSurvivesCorruption()
		{
			var dir = NewDir();
			try
			{
				var file = Path.Combine(dir, "recently-used.xbel");
				File.WriteAllText(file, """
					<?xml version="1.0" encoding="UTF-8"?>
					<xbel version="1.0" xmlns:bookmark="http://www.freedesktop.org/standards/desktop-bookmarks" xmlns:mime="http://www.freedesktop.org/standards/shared-mime-info">
					  <bookmark href="file:///home/u/doc.pdf" added="2024-01-01T10:00:00Z" modified="2024-01-01T10:00:00Z" visited="2024-01-01T10:00:00Z">
					    <info><metadata owner="http://freedesktop.org"><mime:mime-type type="application/pdf"/></metadata></info>
					  </bookmark>
					  <bookmark href="https://example.org/x" added="2024-01-02T10:00:00Z" modified="2024-01-02T10:00:00Z" visited="2024-01-02T10:00:00Z"/>
					</xbel>
					""");

				using var store = new XbelRecentFilesStore(dir);
				Assert.AreEqual(1, store.Read().Count); // non-file URI is not listed
				store.Add("/tmp/new.txt");
				StringAssert.Contains(File.ReadAllText(file), "https://example.org/x");
				Assert.AreEqual("/tmp/new.txt", store.Read()[0].Path);

				File.WriteAllText(file, "not xml");
				Assert.AreEqual(0, store.Read().Count);
				Assert.IsTrue(store.Add("/tmp/again.txt"));
				Assert.AreEqual(1, store.Read().Count);
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}

		[TestMethod]
		public void RejectsRelativePaths()
		{
			var dir = NewDir();
			try
			{
				using var store = new XbelRecentFilesStore(dir);
				Assert.IsFalse(store.Add("relative.txt"));
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}
	}
}
