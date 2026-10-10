// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Files.App.Helpers
{
	/// <summary>
	/// Lets one open of a given target run at a time. A double click can raise the open command twice before the first
	/// navigation finishes, which would push the same location onto the back stack twice.
	/// </summary>
	internal sealed class InFlightOpenGate
	{
		private readonly object gate = new();
		private readonly HashSet<string> inFlight = new(StringComparer.Ordinal);

		/// <summary>Returns a lease to dispose when the open finished, or null when the same target is already being opened.</summary>
		public IDisposable? TryEnter(string key)
		{
			lock (gate)
				return inFlight.Add(key) ? new Lease(this, key) : null;
		}

		private sealed class Lease(InFlightOpenGate owner, string key) : IDisposable
		{
			private int disposed;

			public void Dispose()
			{
				if (Interlocked.Exchange(ref disposed, 1) == 0)
					lock (owner.gate)
						owner.inFlight.Remove(key);
			}
		}
	}
}
