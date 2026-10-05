// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Archives;
using Files.Platform.Linux.Previews;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Previews
{
	[TestClass]
	[SupportedOSPlatform("linux")]
	public sealed class PreviewHardeningTests
	{
		[TestMethod]
		public async Task FifoPreviewOpenAndArchiveListingDoNotBlock()
		{
			var directory = Path.Combine(Path.GetTempPath(), "files-preview-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				var path = Path.Combine(directory, "notes.txt");
				var start = new ProcessStartInfo("mkfifo") { UseShellExecute = false };
				start.ArgumentList.Add(path);
				using (var process = Process.Start(start)!)
				{
					try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
					finally
					{
						if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
					}
					Assert.AreEqual(0, process.ExitCode);
				}
				await Task.Run(() => Assert.Throws<IOException>(() => PreviewFile.OpenRead(path)))
					.WaitAsync(TimeSpan.FromSeconds(2));
				await Assert.ThrowsAsync<IOException>(() => new LinuxArchiveService().ListPreviewAsync(path)
					.WaitAsync(TimeSpan.FromSeconds(2)));
			}
			finally { Directory.Delete(directory, recursive: true); }
		}

		[TestMethod]
		public void PreviewOpenRejectsSymlinksAndDirectoriesAndReadsRegularFiles()
		{
			var directory = Path.Combine(Path.GetTempPath(), "files-preview-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				var path = Path.Combine(directory, "text");
				File.WriteAllText(path, "hello");
				var link = Path.Combine(directory, "link");
				File.CreateSymbolicLink(link, path);
				Assert.Throws<IOException>(() => PreviewFile.OpenRead(link));
				Assert.Throws<IOException>(() => PreviewFile.OpenRead(directory));
				using var stream = PreviewFile.OpenRead(path);
				using var reader = new StreamReader(stream);
				Assert.AreEqual("hello", reader.ReadToEnd());
			}
			finally { Directory.Delete(directory, recursive: true); }
		}

		[TestMethod]
		public void OversizedId3TagIsRejectedBeforeTagLibAllocation()
		{
			byte[] header = [73, 68, 51, 4, 0, 0, 127, 127, 127, 127];
			AssertSmallRejection(header, ".mp3");
			byte[] valid = [73, 68, 51, 4, 0, 0, 0, 0, 0, 1, 0];
			MediaPreviewInput.Validate(valid, ".mp3");
		}

		[TestMethod]
		public void OversizedAndNestedMp4BoxesAreRejectedBeforeTagLibAllocation()
		{
			byte[] box = [127, 255, 255, 255, 100, 97, 116, 97];
			AssertSmallRejection(box, ".m4a");
			byte[] nested = [0, 0, 0, 16, 109, 111, 111, 118, .. box];
			AssertSmallRejection(nested, ".mp4");
			byte[] extended = [0, 0, 0, 1, 100, 97, 116, 97, 255, 255, 255, 255, 255, 255, 255, 255];
			AssertSmallRejection(extended, ".mov");
			MediaPreviewInput.Validate([0, 0, 0, 8, 102, 116, 121, 112], ".m4a");
		}

		[TestMethod]
		public void Mp4OffsetCountCannotAllocateBeyondItsBox()
		{
			byte[] box = [0, 0, 0, 16, 115, 116, 99, 111, 0, 0, 0, 0, 127, 255, 255, 255];
			AssertSmallRejection(box, ".mp4");
			BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12), 0);
			MediaPreviewInput.Validate(box, ".mp4");
		}

		[TestMethod]
		public async Task OversizedMediaSnapshotAndCancellationAreRejected()
		{
			using var stream = new MemoryStream();
			stream.SetLength(MediaPreviewInput.MaxBytes + 1L);
			await Assert.ThrowsAsync<InvalidDataException>(() => MediaPreviewInput.ReadAsync(stream, ".mp3"));
			await Assert.ThrowsAsync<OperationCanceledException>(() => MediaPreviewInput.ReadAsync(stream, ".mp3", new CancellationToken(true)));
		}

		[TestMethod]
		public void EmbeddedId3IsBoundedByItsRiffChunk()
		{
			byte[] riff = [82, 73, 70, 70, 22, 0, 0, 0, 87, 65, 86, 69,
				73, 68, 51, 32, 10, 0, 0, 0, 73, 68, 51, 4, 0, 0, 127, 127, 127, 127];
			AssertSmallRejection(riff, ".wav");
			riff[26] = riff[27] = riff[28] = riff[29] = 0;
			MediaPreviewInput.Validate(riff, ".wav");
		}

		[TestMethod]
		public void TagSignaturesInsideMediaPayloadAreNotParsedAsTags()
		{
			MediaPreviewInput.Validate([1, 2, 3, 73, 68, 51, 4, 0, 0, 127, 127, 127, 127], ".mp3");
		}

		[TestMethod]
		public void CoverPayloadSizesCannotAllocateBeyondTheirMetadata()
		{
			var flacPicture = new byte[32];
			BinaryPrimitives.WriteUInt32BigEndian(flacPicture.AsSpan(28), int.MaxValue);
			Assert.Throws<InvalidDataException>(() => MediaPreviewInput.ValidateFlacPicture(flacPicture));
			byte[] asfPicture = [3, 255, 255, 255, 127, 0, 0, 0, 0];
			Assert.Throws<InvalidDataException>(() => MediaPreviewInput.ValidateAsfPicture(asfPicture));
		}

		[TestMethod]
		public void Mp4DescriptorLengthsCannotOverflowIntoAnAllocation()
		{
			byte[] box = [0, 0, 0, 18, 101, 115, 100, 115, 0, 0, 0, 0, 3, 135, 255, 255, 255, 127];
			AssertSmallRejection(box, ".m4a");
		}

		private static void AssertSmallRejection(byte[] bytes, string extension)
		{
			// Warm exception/JIT paths, then check that attacker-declared sizes do not drive allocation.
			Assert.Throws<InvalidDataException>(() => MediaPreviewInput.Validate(bytes, extension));
			var before = GC.GetAllocatedBytesForCurrentThread();
			Assert.Throws<InvalidDataException>(() => MediaPreviewInput.Validate(bytes, extension));
			Assert.IsLessThan(64 * 1024L, GC.GetAllocatedBytesForCurrentThread() - before);
		}

		[TestMethod]
		[DataRow(0x200B)]
		[DataRow(0x200E)]
		[DataRow(0x200F)]
		[DataRow(0x202A)]
		[DataRow(0x202E)]
		[DataRow(0x2066)]
		[DataRow(0x2067)]
		[DataRow(0x2068)]
		[DataRow(0x2069)]
		[DataRow(0xFEFF)]
		public void InvisibleArchiveNameCharactersAreEscaped(int scalar)
		{
			var result = PreviewEntryName.Sanitize("photo" + char.ConvertFromUtf32(scalar) + "gpj.exe");
			Assert.AreEqual($"photo\\u{{{scalar:X4}}}gpj.exe", result);
		}

		[TestMethod]
		public void ArchiveNameTruncationPreservesSurrogatePairsAndEscapes()
		{
			Assert.AreEqual(new string('a', 1022) + "…", PreviewEntryName.Sanitize(new string('a', 1022) + "😀suffix"));
			Assert.AreEqual(new string('a', 1021) + "😀…", PreviewEntryName.Sanitize(new string('a', 1021) + "😀suffix"));
			Assert.AreEqual("a\\u{000A}b", PreviewEntryName.Sanitize("a\nb"));
		}
	}
}
