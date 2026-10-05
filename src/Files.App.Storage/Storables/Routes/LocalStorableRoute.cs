// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Core.Storage.Contracts;
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
		public Task<StorableResult> TryGetAsync(string path, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (string.IsNullOrEmpty(path) || path.Contains('\0') || !Path.IsPathFullyQualified(path))
				return Task.FromResult(StorableResult.NotMine);

			return Task.FromResult(Resolve(Path.TrimEndingDirectorySeparator(path)));
		}

		private static StorableResult Resolve(string path)
		{
			try
			{
				// Exists checks follow symlinks, so a link to a directory resolves as a folder;
				// a dangling link still exists as an entry and resolves as a file.
				if (Directory.Exists(path))
					return StorableResult.Success(new SystemFolder(path));
				if (File.Exists(path))
					return StorableResult.Success(new SystemFile(path));

				// Exists hides the reason; stat again to tell a missing item from an inaccessible one
				File.GetAttributes(path);
				return StorableResult.Error;
			}
			catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
			{
				return StorableResult.NotFound;
			}
			catch (UnauthorizedAccessException)
			{
				return StorableResult.AccessDenied;
			}
			catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
			{
				return StorableResult.Error;
			}
		}
	}
}
