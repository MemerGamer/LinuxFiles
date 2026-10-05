// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Secrets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Files.App.Storage
{
	public static class FtpManager
	{
		/// <summary>
		/// Credentials by host. Setting an entry also saves the password in the secret store, when one is configured.
		/// </summary>
		public static readonly FtpCredentialCache Credentials = new();

		public static readonly NetworkCredential Anonymous = new("anonymous", "anonymous");

		/// <summary>
		/// The store passwords are persisted in. Set once at startup; passwords are never written anywhere else.
		/// </summary>
		public static ISecretStore? SecretStore { get; set; }
	}

	/// <summary>
	/// Session cache of FTP credentials backed by <see cref="FtpManager.SecretStore"/>.
	/// </summary>
	public sealed class FtpCredentialCache : IReadOnlyDictionary<string, NetworkCredential>
	{
		private const string LastUserAccount = "(last user)";

		private readonly Dictionary<string, NetworkCredential> _session = new(StringComparer.OrdinalIgnoreCase);
		private readonly object _lock = new();

		public NetworkCredential this[string host]
		{
			get => TryGetValue(host, out var credential) ? credential : throw new KeyNotFoundException();
			set => Set(host, value, persist: true);
		}

		/// <summary>
		/// Keeps <paramref name="credential"/> for this session only (for example credentials typed into a URL).
		/// </summary>
		public void SetSessionOnly(string host, NetworkCredential credential) => Set(host, credential, persist: false);

		public NetworkCredential Get(string host, NetworkCredential defaultValue)
			=> TryGetValue(host, out var credential) ? credential : defaultValue;

		public void Remove(string host)
		{
			lock (_lock)
			{
				_session.Remove(host);
			}

			if (FtpManager.SecretStore is { } store)
			{
				try
				{
					var resource = GetResource(host);
					if (store.Get(resource, LastUserAccount) is { } user)
						store.Delete(resource, user);
					store.Delete(resource, LastUserAccount);
				}
				catch (Exception)
				{
					// Secret store unavailable; nothing persisted to remove
				}
			}
		}

		public bool TryGetValue(string host, out NetworkCredential value)
		{
			lock (_lock)
			{
				if (_session.TryGetValue(host, out value!))
					return true;
			}

			if (FtpManager.SecretStore is { } store)
			{
				try
				{
					var resource = GetResource(host);
					if (store.Get(resource, LastUserAccount) is { Length: > 0 } user && store.Get(resource, user) is { } password)
					{
						value = new NetworkCredential(user, password);
						lock (_lock)
						{
							_session[host] = value;
						}

						return true;
					}
				}
				catch (Exception)
				{
					// Secret store unavailable; behave as if nothing was saved
				}
			}

			value = null!;
			return false;
		}

		private void Set(string host, NetworkCredential credential, bool persist)
		{
			lock (_lock)
			{
				_session[host] = credential;
			}

			if (!persist || FtpManager.SecretStore is not { } store || string.IsNullOrEmpty(credential.UserName))
				return;

			try
			{
				var resource = GetResource(host);
				store.Save(resource, credential.UserName, credential.Password);
				store.Save(resource, LastUserAccount, credential.UserName);
			}
			catch (Exception)
			{
				// Keep the credential for this session when the store can't persist it
			}
		}

		private static string GetResource(string host) => "Files FTP " + host.ToLowerInvariant();

		public bool ContainsKey(string host) => TryGetValue(host, out _);

		public IEnumerable<string> Keys
		{
			get { lock (_lock) { return _session.Keys.ToArray(); } }
		}

		public IEnumerable<NetworkCredential> Values
		{
			get { lock (_lock) { return _session.Values.ToArray(); } }
		}

		public int Count
		{
			get { lock (_lock) { return _session.Count; } }
		}

		public IEnumerator<KeyValuePair<string, NetworkCredential>> GetEnumerator()
		{
			KeyValuePair<string, NetworkCredential>[] snapshot;
			lock (_lock)
			{
				snapshot = _session.ToArray();
			}

			return ((IEnumerable<KeyValuePair<string, NetworkCredential>>)snapshot).GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
