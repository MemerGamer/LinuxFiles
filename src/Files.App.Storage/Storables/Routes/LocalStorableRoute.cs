// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Core.Storage.Contracts;
using OwlCore.Storage;
using OwlCore.Storage.System.IO;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.Storage.Storables
{
	/// <summary>
	/// Resolves fully qualified local file system paths to <see cref="SystemFile"/> and <see cref="SystemFolder"/>.
	/// </summary>
	/// <remarks>
	/// This is the catch-all route for native paths, so it uses <see cref="DefaultOrder"/>; routes for paths that look local
	/// but need special handling (such as entries inside archives) must use a lower order.
	/// </remarks>
	public sealed class LocalStorableRoute : IStorableRoute
	{
		/// <summary>
		/// The order of the local route.
		/// </summary>
		public const int DefaultOrder = 10_000;

		/// <inheritdoc/>
		public int Order => DefaultOrder;

		/// <inheritdoc/>
		public bool CanResolve(string path)
		{
			return !string.IsNullOrEmpty(path) && !path.Contains('\0') && Path.IsPathFullyQualified(path);
		}

		/// <inheritdoc/>
		public Task<IStorable?> TryGetAsync(string path, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (!CanResolve(path))
				return Task.FromResult<IStorable?>(null);

			var normalized = Path.TrimEndingDirectorySeparator(path);

			// Exists checks follow symlinks, so a link to a directory resolves as a folder;
			// a dangling link still exists as an entry and resolves as a file.
			IStorable? storable;
			try
			{
				storable = Directory.Exists(normalized)
					? new SystemFolder(normalized)
					: File.Exists(normalized)
						? new SystemFile(normalized)
						: null;
			}
			catch (IOException)
			{
				// Removed or replaced between the check and the constructor's own validation
				storable = null;
			}

			return Task.FromResult<IStorable?>(storable);
		}
	}
}
