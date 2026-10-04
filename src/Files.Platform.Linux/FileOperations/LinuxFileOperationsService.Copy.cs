// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.Platform.Abstractions.FileOperations;

namespace Files.Platform.Linux.FileOperations
{
	public sealed partial class LinuxFileOperationsService
	{
		private const int CopyBufferSize = 1024 * 1024;

		private async Task<Outcome> CopyTopLevelAsync(string source, string destinationDirectory, FileOperationContext context)
		{
			var name = Path.GetFileName(source);
			var sourceKind = FileSystemEntry.GetKind(source);
			if (sourceKind == EntryKind.None)
				return Outcome.Fail(FileOperationErrorKind.NotFound, "The source does not exist.", source);

			var followedKind = sourceKind == EntryKind.Symlink && context.FollowSymlinks ? FileSystemEntry.GetKindFollowing(source) : sourceKind;
			var destinationCanonical = FileSystemEntry.Canonicalize(destinationDirectory);

			if (followedKind == EntryKind.Directory)
			{
				var sourceCanonical = sourceKind == EntryKind.Symlink ? FileSystemEntry.Canonicalize(source) : FileSystemEntry.CanonicalizeEntry(source);
				if (FileSystemEntry.IsSameOrInside(destinationCanonical, sourceCanonical))
					return Outcome.Fail(FileOperationErrorKind.InvalidDestination, "A folder cannot be copied into itself.", destinationDirectory);
			}

			var destination = Path.Combine(destinationDirectory, name);
			var sourceParent = FileSystemEntry.Canonicalize(Path.GetDirectoryName(source)!);
			if (sourceParent == destinationCanonical)
			{
				// Copying next to the original always keeps both
				var unique = FileNameGenerator.GenerateUniqueName(destinationDirectory, name, followedKind == EntryKind.Directory);
				destination = Path.Combine(destinationDirectory, unique);
			}

			return await CopyEntryAsync(source, destination, context, verifySource: false).ConfigureAwait(false);
		}

		/// <summary>Copies one entry (recursively for folders) to <paramref name="destination"/>, applying the conflict policy.</summary>
		private async Task<Outcome> CopyEntryAsync(string source, string destination, FileOperationContext context, bool verifySource)
		{
			context.CancellationToken.ThrowIfCancellationRequested();

			var sourceKind = FileSystemEntry.GetKind(source);
			if (sourceKind == EntryKind.Symlink && context.FollowSymlinks)
				sourceKind = FileSystemEntry.GetKindFollowing(source);

			if (sourceKind == EntryKind.None)
				return Outcome.Fail(FileOperationErrorKind.NotFound, "The source does not exist (or is a broken link).", source);

			try
			{
				ThrowIfInvalidName(destination);

				var sourceIsDirectory = sourceKind == EntryKind.Directory;
				var resolved = await ResolveConflictAsync(context, source, destination, sourceIsDirectory, FileSystemEntry.GetKind(destination)).ConfigureAwait(false);
				if (resolved.Early is { } early)
					return early;

				destination = resolved.Destination;
				ThrowIfInvalidName(destination);

				return sourceKind switch
				{
					EntryKind.Directory => await CopyDirectoryAsync(source, destination, context, verifySource).ConfigureAwait(false),
					EntryKind.Symlink => CopyLink(source, destination, resolved.Replace, context),
					_ => await CopyFileAsync(source, destination, resolved.Replace, context, verifySource).ConfigureAwait(false),
				};
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex) when (ex is not OutOfMemoryException)
			{
				return Outcome.FromException(ex, ex is FileOperationException ? destination : source);
			}
		}

		private async Task<Outcome> CopyDirectoryAsync(string source, string destination, FileOperationContext context, bool verifySource)
		{
			string? canonical = null;
			if (context.FollowSymlinks)
			{
				canonical = FileSystemEntry.Canonicalize(source);
				if (!context.ActiveDirectories.Add(canonical))
					return Outcome.Skipped();
			}

			try
			{
				var created = FileSystemEntry.GetKind(destination) == EntryKind.None;
				var metadata = DirectoryMetadata.Capture(source);
				if (created)
					Directory.CreateDirectory(destination);

				var result = Outcome.Success(destination);
				foreach (var child in Directory.EnumerateFileSystemEntries(source, "*", FileSystemEntry.AllEntries).ToList())
				{
					var childOutcome = await CopyEntryAsync(child, Path.Combine(destination, Path.GetFileName(child)), context, verifySource).ConfigureAwait(false);
					result = result.Combine(childOutcome, destination);
				}

				if (created)
					metadata.ApplyTo(destination);

				context.ItemDone(source);
				return result;
			}
			finally
			{
				if (canonical is not null)
					context.ActiveDirectories.Remove(canonical);
			}
		}

		private readonly record struct DirectoryMetadata(UnixFileMode Mode, DateTime LastWriteUtc)
		{
			/// <summary>Reads the metadata before the folder content is touched, since moving content changes the source time.</summary>
			public static DirectoryMetadata Capture(string path)
				=> new(UnixMode.Get(path), Directory.GetLastWriteTimeUtc(path));

			public void ApplyTo(string path)
			{
				try
				{
					UnixMode.Set(path, Mode);
					Directory.SetLastWriteTimeUtc(path, LastWriteUtc);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					// Metadata is best effort for folders; the content was copied
				}
			}
		}

		private static Outcome CopyLink(string source, string destination, bool replace, FileOperationContext context)
		{
			var target = new FileInfo(source).LinkTarget ?? throw new FileNotFoundException("The link no longer exists.", source);
			if (!replace)
			{
				File.CreateSymbolicLink(destination, target);
			}
			else
			{
				var temporary = TemporaryPathNextTo(destination);
				try
				{
					File.CreateSymbolicLink(temporary, target);
					File.Move(temporary, destination, true);
				}
				finally
				{
					if (FileSystemEntry.GetKind(temporary) != EntryKind.None)
						TryDelete(temporary);
				}
			}

			context.ItemDone(source);
			return Outcome.Success(destination);
		}

		private static async Task<Outcome> CopyFileAsync(string source, string destination, bool replace, FileOperationContext context, bool verifySource)
		{
			var temporary = TemporaryPathNextTo(destination);
			var length = new FileInfo(source).Length;
			long copied = 0;
			var completed = false;

			try
			{
				var sourceTime = File.GetLastWriteTimeUtc(source);
				var sourceMode = UnixMode.Get(source);

				var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
				await using (input.ConfigureAwait(false))
				{
					var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous);
					await using (output.ConfigureAwait(false))
					{
						var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
						try
						{
							int read;
							while ((read = await input.ReadAsync(buffer.AsMemory(0, CopyBufferSize), context.CancellationToken).ConfigureAwait(false)) > 0)
							{
								await output.WriteAsync(buffer.AsMemory(0, read), context.CancellationToken).ConfigureAwait(false);
								copied += read;
								context.AddBytes(read, source);
							}
						}
						finally
						{
							ArrayPool<byte>.Shared.Return(buffer);
						}

						await output.FlushAsync(context.CancellationToken).ConfigureAwait(false);
						output.Flush(true);
					}
				}

				if (new FileInfo(temporary).Length != copied)
					throw new FileOperationException(FileOperationErrorKind.VerificationFailed, "The copied file does not have the expected size.", destination);

				if (verifySource && new FileInfo(source).Length != copied)
					throw new FileOperationException(FileOperationErrorKind.VerificationFailed, "The source changed while it was being copied.", source);

				UnixMode.Set(temporary, sourceMode);
				File.SetLastWriteTimeUtc(temporary, sourceTime);

				context.CancellationToken.ThrowIfCancellationRequested();
				MoveFile(temporary, destination, replace);
				completed = true;
				return Outcome.Success(destination);
			}
			finally
			{
				if (!completed)
					TryDelete(temporary);

				context.AddBytes(Math.Max(0, length - copied), source);
				context.ItemDone(source);
			}
		}

		/// <summary>
		/// Moves a file without ever replacing an existing destination unless asked. When the source cannot be unlinked, .NET may leave a
		/// hard link at the destination; that leftover is removed so a failed move has no side effect.
		/// </summary>
		private static void MoveFile(string source, string destination, bool replace)
		{
			var destinationExisted = FileSystemEntry.GetKind(destination) != EntryKind.None;
			try
			{
				File.Move(source, destination, replace);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				if (!replace
					&& !destinationExisted
					&& FileOperationErrors.Classify(ex) != FileOperationErrorKind.AlreadyExists
					&& FileSystemEntry.GetKind(source) == EntryKind.File
					&& FileSystemEntry.GetKind(destination) == EntryKind.File)
				{
					TryDelete(destination);
				}

				throw;
			}
		}

		private static void TryDelete(string path)
		{
			try
			{
				File.Delete(path);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				// Nothing more can be done for a leftover temporary file
			}
		}
	}
}
