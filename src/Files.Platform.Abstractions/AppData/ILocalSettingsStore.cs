// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Abstractions
{
	/// <summary>
	/// A small persistent key/value store replacing WinRT <c>ApplicationData.Current.LocalSettings</c>.
	/// Supported value types are <see cref="string"/>, <see cref="bool"/>, <see cref="int"/>,
	/// <see cref="long"/> and <see cref="double"/>; other types throw <see cref="NotSupportedException"/>.
	/// </summary>
	public interface ILocalSettingsStore
	{
		/// <summary>
		/// Determines whether the store contains the key.
		/// </summary>
		bool ContainsKey(string key);

		/// <summary>
		/// Gets the value for the key, returning false when it is missing or has a different type.
		/// </summary>
		bool TryGetValue<T>(string key, out T? value);

		/// <summary>
		/// Gets the value for the key, or <paramref name="defaultValue"/> when it is missing or has a different type.
		/// </summary>
		T Get<T>(string key, T defaultValue);

		/// <summary>
		/// Sets the value for the key and persists it.
		/// </summary>
		void Set<T>(string key, T value);

		/// <summary>
		/// Removes the key, returning true when it existed.
		/// </summary>
		bool Remove(string key);

		/// <summary>
		/// Gets a named sub-store (replaces <c>ApplicationDataContainer</c>); created lazily on first write.
		/// </summary>
		ILocalSettingsStore GetContainer(string name);

		/// <summary>
		/// Deletes a named sub-store and all of its values.
		/// </summary>
		void DeleteContainer(string name);
	}
}
