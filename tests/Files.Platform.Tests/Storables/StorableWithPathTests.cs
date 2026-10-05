// Copyright (c) Files Community
// Licensed under the MIT License.

// The linked FilesystemItemType.cs relies on Files.App's implicit System import
global using System;

using Files.App.Data.Enums;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OwlCore.Storage.Memory;

namespace Windows.Storage
{
	// Stand-in for the WinRT type that IStorageItemWithPath.Item exposes
	public interface IStorageItem
	{
	}
}

namespace Files.Platform.Tests.Storables
{
	[TestClass]
	public sealed class StorableWithPathTests
	{
		[TestMethod]
		public void Name_PrefersStorableName()
		{
			var item = StorableWithPath.FromStorable("Home", new MemoryFolder("Home", "Home folder"));

			Assert.AreEqual("Home folder", item.Name);
			Assert.AreEqual(FilesystemItemType.Directory, item.ItemType);
		}

		[TestMethod]
		[DataRow("/tmp/dir/", "dir")]
		[DataRow("/tmp/file.txt", "file.txt")]
		public void Name_WithoutStorable_UsesLastPathSegment(string path, string expected)
		{
			Assert.AreEqual(expected, new StorableWithPath(path, FilesystemItemType.File).Name);
		}

		[TestMethod]
		public void Constructor_RejectsMismatchedKind()
		{
			Assert.ThrowsExactly<ArgumentException>(() => new StorableWithPath("/x", FilesystemItemType.File, new MemoryFolder("/x", "x")));
			Assert.ThrowsExactly<ArgumentException>(() => new StorableWithPath(string.Empty, FilesystemItemType.File));
		}
	}
}
