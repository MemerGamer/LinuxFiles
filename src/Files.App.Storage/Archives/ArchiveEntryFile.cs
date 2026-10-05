// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Archives;
using Files.Shared.Helpers;
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
	/// <summary>A regular archive member. The caller owns the returned bounded stream.</summary>
	public sealed class ArchiveEntryFile : IChildFile
	{
		private readonly ArchiveContext context;
		private readonly ArchiveFolder parent;
		public ArchiveEntryInfo Entry { get; }
		public string Id => context.Path + "/" + Entry.Path;
		public string Name => ArchiveDisplayName.Escape(Entry.Path[(Entry.Path.LastIndexOf('/') + 1)..]);

		internal ArchiveEntryFile(ArchiveContext context, ArchiveEntryInfo entry, ArchiveFolder parent)
		{
			this.context = context;
			Entry = entry;
			this.parent = parent;
		}

		public Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult<IFolder?>(parent);
		}

		public Task<Stream> OpenStreamAsync(FileAccess accessMode, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			// LINUX-TODO(archives): entry modification remains disabled behind IArchiveService.CanWriteEntries.
			if (accessMode != FileAccess.Read)
				throw new NotSupportedException("Archive browsing is read-only.");
			return context.WithPasswordAsync(password => context.Service.OpenEntryAsync(context.Path, Entry.Path, password, cancellationToken), cancellationToken);
		}
	}
}
