// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Archives;
using System.IO;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.Core.Storage.Contracts;
using OwlCore.Storage;

namespace Files.App.Storage.Archives
{
	/// <summary>Routes archive roots and members before the local file system route.</summary>
	public sealed class ArchiveStorableRoute(IArchiveService service, IArchivePasswordPrompt? passwordPrompt = null) : IStorableRoute
	{
		// Session-only; avoids re-prompting on every navigation into an encrypted archive
		private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> passwords = new(StringComparer.Ordinal);

		public int Order => 100;

		public async Task<StorableResult> TryGetAsync(string path, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (string.IsNullOrEmpty(path) || path.Contains('\0') || !Path.IsPathFullyQualified(path))
				return StorableResult.NotMine;
			try
			{
				for (var end = 1; end <= path.Length; end++)
				{
					if (end < path.Length && path[end] != Path.DirectorySeparatorChar)
						continue;
					var container = path[..end];
					if (!service.IsArchiveFileName(container))
						continue;
					var attributes = File.GetAttributes(container);
					if (attributes.HasFlag(FileAttributes.Directory))
						continue;
					var context = new ArchiveContext(container, service, passwordPrompt, passwords);
					var entryPath = ArchiveContext.Normalize(path[end..].TrimStart('/'));
					var listing = await context.ListAsync(cancellationToken).ConfigureAwait(false);
					if (entryPath.Length == 0)
						return StorableResult.Success(new ArchiveFolder(context, string.Empty));
					if (!listing.TryGetValue(entryPath, out var entry))
						return StorableResult.NotFound;
					var parentPath = entryPath.Contains('/') ? entryPath[..entryPath.LastIndexOf('/')] : string.Empty;
					return StorableResult.Success(entry.IsDirectory
						? new ArchiveFolder(context, entryPath)
						: new ArchiveEntryFile(context, entry, new ArchiveFolder(context, parentPath)));
				}
				return StorableResult.NotMine;
			}
			catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return StorableResult.NotFound; }
			catch (UnauthorizedAccessException) { return StorableResult.AccessDenied; }
			catch (OperationCanceledException) { throw; }
			catch (Exception) { return StorableResult.Error; }
		}
	}
}
