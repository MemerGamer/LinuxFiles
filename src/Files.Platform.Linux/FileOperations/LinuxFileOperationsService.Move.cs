// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
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
		private async Task<Outcome> MoveTopLevelAsync(string source, string destinationDirectory, FileOperationContext context, Func<string, string, bool> isSameDevice)
		{
			var sourceKind = FileSystemEntry.GetKind(source);
			if (sourceKind == EntryKind.None)
				return Outcome.Fail(FileOperationErrorKind.NotFound, "The source does not exist.", source);

			var destinationCanonical = FileSystemEntry.Canonicalize(destinationDirectory);
			if (sourceKind == EntryKind.Directory && FileSystemEntry.IsSameOrInside(destinationCanonical, FileSystemEntry.CanonicalizeEntry(source)))
				return Outcome.Fail(FileOperationErrorKind.InvalidDestination, "A folder cannot be moved into itself.", destinationDirectory);

			if (FileSystemEntry.Canonicalize(Path.GetDirectoryName(source)!) == destinationCanonical)
			{
				// Moving into the folder that already contains the item changes nothing
				context.ItemDone(source);
				return Outcome.Success(source);
			}

			return await MoveEntryAsync(source, Path.Combine(destinationDirectory, Path.GetFileName(source)), context, isSameDevice).ConfigureAwait(false);
		}

		private async Task<Outcome> MoveEntryAsync(string source, string destination, FileOperationContext context, Func<string, string, bool> isSameDevice)
		{
			context.CancellationToken.ThrowIfCancellationRequested();

			var sourceKind = FileSystemEntry.GetKind(source);
			if (sourceKind == EntryKind.None)
				return Outcome.Fail(FileOperationErrorKind.NotFound, "The source does not exist.", source);

			try
			{
				ThrowIfInvalidName(destination);

				var sourceIsDirectory = sourceKind == EntryKind.Directory;
				var destinationKind = FileSystemEntry.GetKind(destination);
				var resolved = await ResolveConflictAsync(context, source, destination, sourceIsDirectory, destinationKind).ConfigureAwait(false);
				if (resolved.Early is { } early)
					return early;

				destination = resolved.Destination;
				ThrowIfInvalidName(destination);

				var sameDevice = isSameDevice(source, destination);
				if (!sourceIsDirectory)
				{
					if (sameDevice)
					{
						var length = sourceKind == EntryKind.File ? new FileInfo(source).Length : 0;
						MoveLeaf(source, destination, sourceKind, resolved.Replace);
						context.AddBytes(length, source);
						context.ItemDone(source);
						return Outcome.Success(destination);
					}

					var copy = sourceKind == EntryKind.Symlink
						? CopyLink(source, destination, resolved.Replace, context)
						: await CopyFileAsync(source, destination, resolved.Replace, context, verifySource: true).ConfigureAwait(false);

					if (copy.Status == FileOperationStatus.Succeeded)
						File.Delete(source);

					return copy;
				}

				if (!resolved.Replace && FileSystemEntry.GetKind(destination) == EntryKind.None && sameDevice)
				{
					try
					{
						Directory.Move(source, destination);
						context.ItemDone(source);
						return Outcome.Success(destination);
					}
					catch (IOException) when (FileSystemEntry.GetKind(source) == EntryKind.Directory && FileSystemEntry.GetKind(destination) == EntryKind.None)
					{
						// rename(2) was refused (for example EXDEV); continue with a verified copy and delete
					}
				}

				return await MoveDirectoryContentsAsync(source, destination, context, isSameDevice).ConfigureAwait(false);
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

		/// <summary>Moves a folder entry by entry (merge or cross-device); the source folder is removed only once it is empty.</summary>
		private async Task<Outcome> MoveDirectoryContentsAsync(string source, string destination, FileOperationContext context, Func<string, string, bool> isSameDevice)
		{
			var created = FileSystemEntry.GetKind(destination) == EntryKind.None;
			var metadata = DirectoryMetadata.Capture(source);
			if (created)
				Directory.CreateDirectory(destination);

			var result = Outcome.Success(destination);
			foreach (var child in Directory.EnumerateFileSystemEntries(source, "*", FileSystemEntry.AllEntries).ToList())
			{
				var childOutcome = await MoveEntryAsync(child, Path.Combine(destination, Path.GetFileName(child)), context, isSameDevice).ConfigureAwait(false);
				result = result.Combine(childOutcome, destination);
			}

			if (created)
				metadata.ApplyTo(destination);

			if (result.Status == FileOperationStatus.Succeeded)
			{
				try
				{
					Directory.Delete(source, false);
				}
				catch (IOException) when (Directory.EnumerateFileSystemEntries(source, "*", FileSystemEntry.AllEntriesLenient).Any())
				{
					// Items the user chose to skip remain in the source folder
				}
			}

			context.ItemDone(source);
			return result;
		}

		/// <summary>Renames a file or link; links to folders need Directory.Move, which cannot replace, so the consented destination is removed first.</summary>
		private static void MoveLeaf(string source, string destination, EntryKind sourceKind, bool replace)
		{
			if (sourceKind == EntryKind.Symlink && Directory.Exists(source))
			{
				if (replace)
					File.Delete(destination);

				Directory.Move(source, destination);
				return;
			}

			MoveFile(source, destination, replace);
		}

		private Task<Outcome> DeleteEntryAsync(string path, FileOperationContext context)
			=> Task.FromResult(DeleteEntry(path, context));

		private static Outcome DeleteEntry(string path, FileOperationContext context)
		{
			context.CancellationToken.ThrowIfCancellationRequested();

			try
			{
				switch (FileSystemEntry.GetKind(path))
				{
					case EntryKind.None:
						return Outcome.Fail(FileOperationErrorKind.NotFound, "The item does not exist.", path);

					case EntryKind.Directory:
						{
							var result = Outcome.Success(null);
							foreach (var child in Directory.EnumerateFileSystemEntries(path, "*", FileSystemEntry.AllEntries).ToList())
								result = result.Combine(DeleteEntry(child, context), null);

							if (result.Status == FileOperationStatus.Succeeded)
								Directory.Delete(path, false);

							context.ItemDone(path);
							return result;
						}

					default:
						{
							// Links are unlinked, never followed
							var length = FileSystemEntry.GetKind(path) == EntryKind.File ? new FileInfo(path).Length : 0;
							File.Delete(path);
							context.AddBytes(length, path);
							context.ItemDone(path);
							return Outcome.Success(null);
						}
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex) when (ex is not OutOfMemoryException)
			{
				return Outcome.FromException(ex, path);
			}
		}

		private async Task<Outcome> RenameCoreAsync(string path, string newName, FileOperationContext context)
		{
			if (FileNameGenerator.Validate(newName) is { } invalid)
				return Outcome.Fail(invalid, "The name is not valid for the file system.", newName);

			var source = TryNormalize(path);
			if (source is null)
				return Outcome.Fail(FileOperationErrorKind.InvalidName, "The path is not valid or refers to the root.", path);

			var sourceKind = FileSystemEntry.GetKind(source);
			if (sourceKind == EntryKind.None)
				return Outcome.Fail(FileOperationErrorKind.NotFound, "The item does not exist.", source);

			var parent = Path.GetDirectoryName(source)!;
			var destination = Path.Combine(parent, newName);
			if (destination == source)
				return Outcome.Success(source);

			var sourceIsDirectory = sourceKind == EntryKind.Directory;
			var destinationKind = FileSystemEntry.GetKind(destination);

			// A case-only rename on a case-insensitive volume finds "itself" at the destination
			if (destinationKind != EntryKind.None
				&& string.Equals(destination, source, StringComparison.OrdinalIgnoreCase)
				&& !Directory.EnumerateFileSystemEntries(parent, "*", FileSystemEntry.AllEntriesLenient).Any(e => Path.GetFileName(e) == newName))
			{
				var temporary = TemporaryPathNextTo(destination);
				Move(source, temporary, sourceIsDirectory, false);
				Move(temporary, destination, sourceIsDirectory, false);
				return Outcome.Success(destination);
			}

			var resolved = await ResolveConflictAsync(context, source, destination, sourceIsDirectory, destinationKind).ConfigureAwait(false);
			if (resolved.Early is { } early)
				return early;

			if (resolved.Replace && sourceIsDirectory)
				return Outcome.Fail(FileOperationErrorKind.AlreadyExists, "A folder cannot be merged by renaming.", resolved.Destination);

			Move(source, resolved.Destination, sourceIsDirectory, resolved.Replace);
			return Outcome.Success(resolved.Destination);

			static void Move(string from, string to, bool directory, bool overwrite)
			{
				if (directory)
					Directory.Move(from, to);
				else
					MoveLeaf(from, to, FileSystemEntry.GetKind(from), overwrite);
			}
		}
	}
}
