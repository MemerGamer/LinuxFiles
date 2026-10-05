// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Archives
{
	/// <summary>Requests an archive password on demand. Null cancels the operation; passwords are never persisted.</summary>
	public interface IArchivePasswordPrompt
	{
		/// <summary>Requests a password, indicating whether a previous attempt failed.</summary>
		Task<string?> RequestPasswordAsync(string archivePath, bool retry, CancellationToken cancellationToken = default);
	}
}
