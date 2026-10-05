// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Archives;
using System.IO;
using System.Text;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.Core.Storage.Contracts;
using OwlCore.Storage;

namespace Files.App.Storage.Archives
{
	internal sealed class ArchiveContext(string path, IArchiveService service, IArchivePasswordPrompt? prompt, System.Collections.Concurrent.ConcurrentDictionary<string, string>? passwordCache = null)
	{
		private string? password = passwordCache is not null && passwordCache.TryGetValue(path, out var cached) ? cached : null;
		private Dictionary<string, ArchiveEntryInfo>? entries;
		private readonly SemaphoreSlim gate = new(1, 1);
		public string Path { get; } = path;
		public IArchiveService Service { get; } = service;

		public async Task<T> WithPasswordAsync<T>(Func<string?, Task<T>> operation, CancellationToken cancellationToken)
		{
			await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				for (var attempt = 0; ; attempt++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						var result = await operation(password).ConfigureAwait(false);
						if (password is not null && passwordCache is not null)
							passwordCache[Path] = password;
						return result;
					}
					catch (ArchivePasswordException) when (prompt is not null && attempt < 3)
					{
						password = await prompt.RequestPasswordAsync(Path, password is not null, cancellationToken).ConfigureAwait(false);
						if (password is null)
							throw new OperationCanceledException(cancellationToken);
					}
				}
			}
			finally { gate.Release(); }
		}

		public async Task<Dictionary<string, ArchiveEntryInfo>> ListAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (entries is { } cached)
				return cached;
			var listing = await WithPasswordAsync(password => Service.ListForBrowsingAsync(Path, password, cancellationToken: cancellationToken), cancellationToken).ConfigureAwait(false);
			var result = new Dictionary<string, ArchiveEntryInfo>(StringComparer.Ordinal);
			foreach (var entry in listing.Entries)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var name = Normalize(entry.Path);
				if (name.Length == 0 || entry.LinkTarget is not null)
					continue;
				if (!result.TryAdd(name, entry with { Path = name }))
					throw new ArchiveSecurityException("The archive contains ambiguous entry names.");
			}
			foreach (var name in result.Keys.ToArray())
			{
				var parent = name;
				while (parent.LastIndexOf('/') is var index && index >= 0)
				{
					parent = parent[..index];
					if (result.TryGetValue(parent, out var item) && !item.IsDirectory)
						throw new ArchiveSecurityException("An archive file is also used as a directory.");
					if (result.Count >= 10000 && !result.ContainsKey(parent))
						throw new ArchiveSecurityException("The archive hierarchy limit was exceeded.");
					result.TryAdd(parent, new ArchiveEntryInfo(parent, true, 0, 0, null, false));
				}
			}
			entries = result;
			return result;
		}

		internal static string Normalize(string value)
		{
			if (Encoding.UTF8.GetByteCount(value) > 4096)
				throw new ArchiveSecurityException("An archive entry path is too long.");
			var path = value.Replace('\\', '/');
			if (path.StartsWith('/') || path.Contains('\0') || (path.Length >= 2 && path[1] == ':'))
				throw new ArchiveSecurityException("An archive entry has an unsafe path.");
			var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
			if (segments.Length > 128 || segments.Any(segment => Encoding.UTF8.GetByteCount(segment) > 255))
				throw new ArchiveSecurityException("An archive entry path exceeds browsing limits.");
			if (segments.Any(segment => segment == ".."))
				throw new ArchiveSecurityException("An archive entry traverses its parent.");
			return string.Join('/', segments.Where(segment => segment != "."));
		}
	}
}
