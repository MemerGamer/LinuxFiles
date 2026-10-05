// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Abstractions.Secrets
{
	/// <summary>
	/// Stores small secrets (tokens, passwords) for the app, replacing the Windows credential vault.
	/// </summary>
	public interface ISecretStore
	{
		/// <summary>
		/// Gets whether secrets survive a restart of the app (OS keyring) or only live in memory.
		/// </summary>
		bool IsPersistent { get; }

		/// <summary>
		/// Saves (or replaces) the secret of <paramref name="resource"/> and <paramref name="account"/>.
		/// </summary>
		void Save(string resource, string account, string secret);

		/// <summary>
		/// Returns the secret, or <see langword="null"/> when there is none.
		/// </summary>
		string? Get(string resource, string account);

		/// <summary>
		/// Removes the secret. Returns whether one was removed.
		/// </summary>
		bool Delete(string resource, string account);
	}
}
