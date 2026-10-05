// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Archives;
using Files.Shared.Helpers;
using OwlCore.Storage.System.IO;
using System.IO;
using System.Runtime.CompilerServices;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.Core.Storage.Contracts;
using OwlCore.Storage;

namespace Files.App.Storage.Archives
{
	/// <summary>A read-only view of an archive directory, including directories implied by entry names.</summary>
	public sealed class ArchiveFolder : IChildFolder, IGetItem
	{
		private readonly ArchiveContext context;
		internal string EntryPath { get; }
		public string Id => EntryPath.Length == 0 ? context.Path : context.Path + "/" + EntryPath;
		public string Name => ArchiveDisplayName.Escape(EntryPath.Length == 0 ? Path.GetFileName(context.Path) : EntryPath[(EntryPath.LastIndexOf('/') + 1)..]);

		internal ArchiveFolder(ArchiveContext context, string entryPath)
		{
			this.context = context;
			EntryPath = entryPath;
		}

		public Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			IFolder? parent = EntryPath.Length == 0
				? new SystemFolder(Path.GetDirectoryName(context.Path)!)
				: new ArchiveFolder(context, EntryPath.Contains('/') ? EntryPath[..EntryPath.LastIndexOf('/')] : string.Empty);
			return Task.FromResult<IFolder?>(parent);
		}

		public async IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			var entries = await context.ListAsync(cancellationToken).ConfigureAwait(false);
			var prefix = EntryPath.Length == 0 ? string.Empty : EntryPath + "/";
			foreach (var entry in entries)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!entry.Key.StartsWith(prefix, StringComparison.Ordinal) || entry.Key.Length == prefix.Length || entry.Key.AsSpan(prefix.Length).Contains('/'))
					continue;
				if (entry.Value.IsDirectory && type.HasFlag(StorableType.Folder))
					yield return new ArchiveFolder(context, entry.Key);
				else if (!entry.Value.IsDirectory && type.HasFlag(StorableType.File))
					yield return new ArchiveEntryFile(context, entry.Value, this);
			}
		}

		public async Task<IStorableChild> GetItemAsync(string id, CancellationToken cancellationToken = default)
		{
			await foreach (var item in GetItemsAsync(StorableType.All, cancellationToken).ConfigureAwait(false))
				if (item.Id == id)
					return item;
			throw new FileNotFoundException("The archive child was not found.");
		}

		public Task<IFolderWatcher> GetFolderWatcherAsync(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromException<IFolderWatcher>(new NotSupportedException());
		}
	}
}
