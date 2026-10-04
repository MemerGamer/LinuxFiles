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
using Files.Platform.Linux.Native;
using Microsoft.Win32.SafeHandles;

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
		private async Task<Outcome> CopyEntryAsync(string source, string destination, FileOperationContext context, bool verifySource, DirectoryHandle? parent = null)
		{
			context.CancellationToken.ThrowIfCancellationRequested();

			var sourceKind = parent is null ? FileSystemEntry.GetKind(source) : FileSystemEntry.GetKindAt(parent.Descriptor, Path.GetFileName(source));
			if (sourceKind == EntryKind.Symlink && context.FollowSymlinks)
				sourceKind = FileSystemEntry.GetKindFollowing(source);

			if (sourceKind == EntryKind.None)
				return Outcome.Fail(FileOperationErrorKind.NotFound, "The source does not exist (or is a broken link).", source);

			if (sourceKind == EntryKind.Special)
				return Outcome.Fail(FileOperationErrorKind.UnsupportedFileType, "FIFOs, sockets and device files cannot be copied.", source);

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
					EntryKind.Directory => await CopyDirectoryAsync(source, destination, context, verifySource, parent).ConfigureAwait(false),
					EntryKind.Symlink => CopyLink(source, destination, resolved.Replace, context, parent),
					_ => await CopyFileAsync(source, destination, resolved.Replace, context, verifySource, parent).ConfigureAwait(false),
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

		private async Task<Outcome> CopyDirectoryAsync(string source, string destination, FileOperationContext context, bool verifySource, DirectoryHandle? parent)
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
				using var handle = OpenSourceDirectory(source, parent, context);
				var metadata = DirectoryMetadata.Capture(source);
				if (created)
				{
					UnixMode.CreatePrivateDirectory(destination);
					context.Hooks?.DirectoryCreated?.Invoke(destination);
				}

				var result = Outcome.Success(destination);
				foreach (var name in handle.ListNames())
				{
					var childOutcome = await CopyEntryAsync(Path.Combine(source, name), Path.Combine(destination, name), context, verifySource, context.FollowSymlinks ? null : handle).ConfigureAwait(false);
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

		/// <summary>Opens a source folder; unless links are followed, a link swapped in after the kind check is refused.</summary>
		private static DirectoryHandle OpenSourceDirectory(string source, DirectoryHandle? parent, FileOperationContext context)
		{
			context.Hooks?.BeforeOpenSource?.Invoke(source);
			var handle = DirectoryHandle.TryOpen(
				parent?.Descriptor ?? PosixNative.AtFdCwd,
				parent is null ? source : Path.GetFileName(source),
				source,
				!context.FollowSymlinks,
				out var errno);

			if (handle is not null)
				return handle;

			if (!context.FollowSymlinks && PosixNative.IsNotFollowedError(errno))
				throw new FileOperationException(FileOperationErrorKind.VerificationFailed, "The source folder was replaced while it was being copied.", source);

			throw PosixNative.CreateException(errno, source);
		}

		/// <summary>Opens a source file without blocking and verifies on the descriptor that it is a regular file.</summary>
		private static FileStream OpenSourceFile(string source, DirectoryHandle? parent, FileOperationContext context, out PosixStat stat)
		{
			context.Hooks?.BeforeOpenSource?.Invoke(source);
			var flags = PosixNative.NonBlockingFlags | (context.FollowSymlinks ? 0 : PosixNative.ONofollow);
			var descriptor = PosixNative.OpenAt(
				parent?.Descriptor ?? PosixNative.AtFdCwd,
				parent is null ? source : Path.GetFileName(source),
				flags,
				out var errno);

			if (descriptor < 0)
			{
				if (!context.FollowSymlinks && PosixNative.IsNotFollowedError(errno))
					throw new FileOperationException(FileOperationErrorKind.VerificationFailed, "The source was replaced by a link while it was being copied.", source);

				throw PosixNative.CreateException(errno, source);
			}

			if (!PosixNative.TryStat(descriptor, out stat) || !stat.IsRegularFile)
			{
				PosixNative.Close(descriptor);
				throw new FileOperationException(FileOperationErrorKind.UnsupportedFileType, "Only regular files can be copied.", source);
			}

			return new FileStream(new SafeFileHandle(descriptor, true), FileAccess.Read, 1, false);
		}

		private static Outcome CopyLink(string source, string destination, bool replace, FileOperationContext context, DirectoryHandle? parent)
		{
			var target = parent is not null
				? PosixNative.ReadLinkAt(parent.Descriptor, Path.GetFileName(source), source)
				: new FileInfo(source).LinkTarget ?? throw new FileNotFoundException("The link no longer exists.", source);
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

		private static async Task<Outcome> CopyFileAsync(string source, string destination, bool replace, FileOperationContext context, bool verifySource, DirectoryHandle? parent = null)
		{
			var temporary = TemporaryPathNextTo(destination);
			long length = 0;
			long copied = 0;
			long sourceSizeAfter = -1;
			var completed = false;

			try
			{
				var input = OpenSourceFile(source, parent, context, out var sourceStat);
				length = (long)sourceStat.Size;
				var sourceTime = sourceStat.ModifiedUtc;
				var sourceMode = (UnixFileMode)(sourceStat.Mode & 0xFFF);

				await using (input.ConfigureAwait(false))
				{
					// Owner-only until the final mode is applied, so a secret is never briefly readable by others
					var outputOptions = new FileStreamOptions
					{
						Mode = FileMode.CreateNew,
						Access = FileAccess.Write,
						Share = FileShare.None,
						BufferSize = 1,
						Options = FileOptions.Asynchronous,
					};

					if (!OperatingSystem.IsWindows())
						outputOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

					var output = new FileStream(temporary, outputOptions);
					await using (output.ConfigureAwait(false))
					{
						context.Hooks?.TemporaryFileCreated?.Invoke(temporary);
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

					if (PosixNative.TryStat((int)input.SafeFileHandle.DangerousGetHandle(), out var after))
						sourceSizeAfter = (long)after.Size;
				}

				if (new FileInfo(temporary).Length != copied)
					throw new FileOperationException(FileOperationErrorKind.VerificationFailed, "The copied file does not have the expected size.", destination);

				if (verifySource && sourceSizeAfter != copied)
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
