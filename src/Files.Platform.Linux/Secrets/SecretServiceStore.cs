// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Secrets;
using Files.Platform.Linux.DBus;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Files.Platform.Linux.Secrets
{
	/// <summary>
	/// <see cref="ISecretStore"/> on the freedesktop Secret Service (libsecret's backend: GNOME Keyring, KWallet, KeePassXC) over D-Bus.
	/// When no service is reachable or the collection is locked, secrets are kept in memory for the lifetime of the process.
	/// </summary>
	public sealed class SecretServiceStore : ISecretStore
	{
		private const string Service = "org.freedesktop.secrets";
		private const string ServicePath = "/org/freedesktop/secrets";
		private const string DefaultCollection = "/org/freedesktop/secrets/aliases/default";
		private const string ServiceInterface = "org.freedesktop.Secret.Service";
		private const string CollectionInterface = "org.freedesktop.Secret.Collection";
		private const string ItemInterface = "org.freedesktop.Secret.Item";
		private const string LabelProperty = "org.freedesktop.Secret.Item.Label";
		private const string AttributesProperty = "org.freedesktop.Secret.Item.Attributes";
		private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);

		private readonly string? busAddress;
		private readonly ConcurrentDictionary<(string, string), string> memory = new();

		/// <summary>
		/// Creates the store. <paramref name="busAddress"/> null means the user's session bus.
		/// </summary>
		public SecretServiceStore(string? busAddress = null)
		{
			this.busAddress = busAddress;
		}

		/// <inheritdoc/>
		public bool IsPersistent { get; private set; }

		/// <inheritdoc/>
		public void Save(string resource, string account, string secret)
		{
			memory[(resource, account)] = secret;
			// LINUX-TODO(secrets): prompts to unlock a locked keyring are not shown; a locked collection falls back to memory only
			IsPersistent = Run(c => SaveAsync(c, resource, account, secret));
		}

		/// <inheritdoc/>
		public string? Get(string resource, string account)
		{
			var stored = Run(c => GetAsync(c, resource, account), out var found);
			if (found && stored is not null)
				return stored;

			return memory.TryGetValue((resource, account), out var value) ? value : null;
		}

		/// <inheritdoc/>
		public bool Delete(string resource, string account)
		{
			var removedMemory = memory.TryRemove((resource, account), out _);
			var removedRemote = Run(c => DeleteAsync(c, resource, account));
			return removedMemory || removedRemote;
		}

		private bool Run(Func<DBusConnection, Task<bool>> action)
			=> Run(async c => await action(c).ConfigureAwait(false) ? "1" : null, out _) is not null;

		private T? Run<T>(Func<DBusConnection, Task<T?>> action, out bool reachable) where T : class
		{
			var wasReachable = false;
			try
			{
				return RunAsync(action, () => wasReachable = true).GetAwaiter().GetResult();
			}
			catch (Exception ex) when (ex is TimeoutException or DBusExceptionBase or ObjectDisposedException or InvalidOperationException or System.IO.IOException)
			{
				return null;
			}
			finally
			{
				reachable = wasReachable;
			}
		}

		private async Task<T?> RunAsync<T>(Func<DBusConnection, Task<T?>> action, Action markReachable) where T : class
		{
			var connection = await DBusSession.TryConnectAsync(busAddress, CallTimeout).ConfigureAwait(false);
			if (connection is null)
				return null;

			try
			{
				markReachable();
				return await action(connection).WaitAsync(CallTimeout * 2).ConfigureAwait(false);
			}
			finally
			{
				connection.Dispose();
			}
		}

		private static Task<string> OpenSessionAsync(DBusConnection bus)
		{
			using var writer = bus.GetMessageWriter();
			writer.WriteMethodCallHeader(Service, ServicePath, ServiceInterface, "OpenSession", "sv");
			writer.WriteString("plain");
			writer.WriteVariantString(string.Empty);
			return bus.CallMethodAsync(writer.CreateMessage(), static (Message m, object? _) =>
			{
				var reader = m.GetBodyReader();
				reader.ReadVariantValue();
				return reader.ReadObjectPathAsString();
			}, null);
		}

		private static void WriteAttributes(ref MessageWriter writer, string resource, string account)
		{
			var start = writer.WriteDictionaryStart();
			foreach (var (key, value) in new[] { ("application", "Files"), ("resource", resource), ("account", account) })
			{
				writer.WriteDictionaryEntryStart();
				writer.WriteString(key);
				writer.WriteString(value);
			}
			writer.WriteDictionaryEnd(start);
		}

		private static async Task<string?> FindItemAsync(DBusConnection bus, string resource, string account)
		{
			MessageBuffer message;
			{
				var writer = bus.GetMessageWriter();
				try
				{
					writer.WriteMethodCallHeader(Service, ServicePath, ServiceInterface, "SearchItems", "a{ss}");
					WriteAttributes(ref writer, resource, account);
					message = writer.CreateMessage();
				}
				finally
				{
					writer.Dispose();
				}
			}

			var (unlocked, locked) = await bus.CallMethodAsync(message, static (Message m, object? _) =>
			{
				var reader = m.GetBodyReader();
				var unlockedItems = reader.ReadArrayOfObjectPath().Select(p => p.ToString()).ToArray();
				var lockedItems = reader.ReadArrayOfObjectPath().Select(p => p.ToString()).ToArray();
				return (unlockedItems, lockedItems);
			}, null).ConfigureAwait(false);

			return unlocked.FirstOrDefault() ?? locked.FirstOrDefault();
		}

		private static async Task<string?> GetAsync(DBusConnection bus, string resource, string account)
		{
			var item = await FindItemAsync(bus, resource, account).ConfigureAwait(false);
			if (item is null)
				return null;

			var session = await OpenSessionAsync(bus).ConfigureAwait(false);

			MessageBuffer message;
			using (var writer = bus.GetMessageWriter())
			{
				writer.WriteMethodCallHeader(Service, item, ItemInterface, "GetSecret", "o");
				writer.WriteObjectPath(session);
				message = writer.CreateMessage();
			}

			var bytes = await bus.CallMethodAsync(message, static (Message m, object? _) =>
			{
				var reader = m.GetBodyReader();
				reader.AlignStruct();
				reader.ReadObjectPath();
				reader.ReadArrayOfByte();
				var value = reader.ReadArrayOfByte();
				reader.ReadString();
				return value;
			}, null).ConfigureAwait(false);

			return Encoding.UTF8.GetString(bytes);
		}

		private static async Task<bool> SaveAsync(DBusConnection bus, string resource, string account, string secret)
		{
			var session = await OpenSessionAsync(bus).ConfigureAwait(false);

			MessageBuffer message;
			{
				var writer = bus.GetMessageWriter();
				try
				{
					writer.WriteMethodCallHeader(Service, DefaultCollection, CollectionInterface, "CreateItem", "a{sv}(oayays)b");

					var props = writer.WriteDictionaryStart();
					writer.WriteDictionaryEntryStart();
					writer.WriteString(LabelProperty);
					writer.WriteVariantString($"Files: {resource} ({account})");
					writer.WriteDictionaryEntryStart();
					writer.WriteString(AttributesProperty);
					writer.WriteSignature("a{ss}");
					WriteAttributes(ref writer, resource, account);
					writer.WriteDictionaryEnd(props);

					writer.WriteStructureStart();
					writer.WriteObjectPath(session);
					writer.WriteArray(Array.Empty<byte>());
					writer.WriteArray(Encoding.UTF8.GetBytes(secret));
					writer.WriteString("text/plain");
					writer.WriteBool(true);

					message = writer.CreateMessage();
				}
				finally
				{
					writer.Dispose();
				}
			}

			var prompt = await bus.CallMethodAsync(message, static (Message m, object? _) =>
			{
				var reader = m.GetBodyReader();
				reader.ReadObjectPath();
				return reader.ReadObjectPathAsString();
			}, null).ConfigureAwait(false);

			// A prompt means the collection is locked and needs the user to unlock it
			return prompt == "/";
		}

		private static async Task<bool> DeleteAsync(DBusConnection bus, string resource, string account)
		{
			var item = await FindItemAsync(bus, resource, account).ConfigureAwait(false);
			if (item is null)
				return false;

			MessageBuffer message;
			using (var writer = bus.GetMessageWriter())
			{
				writer.WriteMethodCallHeader(Service, item, ItemInterface, "Delete");
				message = writer.CreateMessage();
			}

			var prompt = await bus.CallMethodAsync(message, static (Message m, object? _) => m.GetBodyReader().ReadObjectPathAsString(), null).ConfigureAwait(false);
			return prompt == "/";
		}
	}

	/// <summary>
	/// Registers <see cref="ISecretStore"/>.
	/// </summary>
	public static class SecretsServiceCollectionExtensions
	{
		/// <summary>
		/// Registers <see cref="ISecretStore"/> backed by the Secret Service on the session bus.
		/// </summary>
		public static IServiceCollection AddLinuxSecrets(this IServiceCollection services)
			=> services.AddSingleton<ISecretStore>(_ => new SecretServiceStore());
	}
}
