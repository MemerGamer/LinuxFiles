// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Mime
{
	/// <summary>
	/// Queries installed applications and their MIME type associations.
	/// </summary>
	public interface IApplicationRegistry
	{
		/// <summary>
		/// Lists the visible applications that can open a MIME type, preferred first.
		/// </summary>
		Task<IReadOnlyList<DesktopApplication>> GetApplicationsForMimeTypeAsync(string mimeType, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets the default application for a MIME type, or null when none is associated.
		/// </summary>
		Task<DesktopApplication?> GetDefaultApplicationAsync(string mimeType, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets an installed application by desktop file ID, or null when not installed.
		/// </summary>
		Task<DesktopApplication?> GetApplicationAsync(string desktopId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Persists the default application for a MIME type in the user's mimeapps.list.
		/// </summary>
		Task SetDefaultApplicationAsync(string mimeType, string desktopId, CancellationToken cancellationToken = default);
	}
}
