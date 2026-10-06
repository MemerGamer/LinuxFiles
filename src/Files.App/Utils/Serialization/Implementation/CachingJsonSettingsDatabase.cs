// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Concurrent;

namespace Files.App.Utils.Serialization.Implementation
{
	internal sealed class CachingJsonSettingsDatabase : DefaultJsonSettingsDatabase
	{
		private readonly object _cacheLock = new();
		private readonly HashSet<string> _dirtyKeys = [];
		private ConcurrentDictionary<string, JsonElement>? _settingsCache;

		public CachingJsonSettingsDatabase(
			ISettingsSerializer settingsSerializer,
			IJsonSettingsSerializer jsonSettingsSerializer,
			JsonSerializerContext jsonSerializerContext)
			: base(settingsSerializer, jsonSettingsSerializer, jsonSerializerContext)
		{
		}

		public override TValue? GetValue<TValue>(string key, TValue? defaultValue = default) where TValue : default
		{
			lock (_cacheLock)
			{
				_settingsCache ??= GetFreshSettings();

				if (_settingsCache.TryGetValue(key, out var objVal))
					return GetValueFromElement<TValue>(objVal) ?? defaultValue;

				// Reading a default does not count as changing the setting.
				_settingsCache.TryAdd(key, GetElementFromValue(defaultValue));
				return defaultValue;
			}
		}

		public override bool SetValue<TValue>(string key, TValue? newValue) where TValue : default
		{
			lock (_cacheLock)
			{
				_settingsCache ??= GetFreshSettings();
				var newElement = GetElementFromValue(newValue);

				if (_settingsCache.TryGetValue(key, out var oldElement) && JsonElement.DeepEquals(oldElement, newElement))
					return _dirtyKeys.Count > 0 && SaveChangedSettings();

				_settingsCache[key] = newElement;
				_dirtyKeys.Add(key);
				return SaveChangedSettings();
			}
		}

		public override bool RemoveKey(string key)
		{
			lock (_cacheLock)
			{
				_settingsCache ??= GetFreshSettings();

				if (!_settingsCache.TryRemove(key, out _) && !_dirtyKeys.Contains(key))
					return false;

				_dirtyKeys.Add(key);
				return SaveChangedSettings();
			}
		}

		private bool SaveChangedSettings()
		{
			return SettingsSerializer.WithWriteLock(() =>
			{
				// Never let an empty, unreadable or corrupt file wipe the keys this window still holds.
				var settings = TryGetFreshSettings() ?? new ConcurrentDictionary<string, JsonElement>(_settingsCache!);
				foreach (var key in _dirtyKeys)
				{
					if (_settingsCache!.TryGetValue(key, out var value))
						settings[key] = value;
					else
						settings.TryRemove(key, out _);
				}

				if (!SaveSettings(settings))
					return false;

				_settingsCache = settings;
				_dirtyKeys.Clear();
				return true;
			});
		}

		public override bool ImportSettings(object? import)
		{
			lock (_cacheLock)
			{
				return SettingsSerializer.WithWriteLock(() =>
				{
					if (!base.ImportSettings(import))
						return false;

					_settingsCache = GetFreshSettings();
					_dirtyKeys.Clear();
					return true;
				});
			}
		}
	}
}
