// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Instance
{
	/// <summary>
	/// Makes sure only one instance of the app runs per user session and forwards later launches to it.
	/// </summary>
	public interface ISingleInstanceService : IAsyncDisposable
	{
		/// <summary>
		/// Gets raised, on a thread pool thread, when a request arrives from a second launch or another application.
		/// Requests received before a handler is attached are queued and delivered when the first handler subscribes.
		/// </summary>
		event EventHandler<InstanceRequest>? RequestReceived;

		/// <summary>
		/// Tries to become the primary instance. If another instance already is, <paramref name="launchRequest"/> is
		/// forwarded to it and false is returned: the caller should exit. Returns true if this process is now the primary instance.
		/// </summary>
		/// <remarks>
		/// If the primary cannot be reached, this process continues as primary.
		/// </remarks>
		Task<bool> TryBecomePrimaryAsync(InstanceRequest launchRequest, CancellationToken cancellationToken = default);
	}
}
