// Copyright (c) Files Community
// Licensed under the MIT License.

// LINUX-TODO(registry): In-memory emulation of Microsoft.Win32.Registry so the Windows-registry backed stores
// (layout preferences, file tags, launch counters) and registry probes (cloud drives, WSL, policies) do not throw on Linux.
// Source types shadow the (non-functional on Linux) framework types of the same name. Values are not persisted across runs yet;
// the proper fix is to move those stores behind Files.Platform (IAppDataPaths.FileTagsSettingsFilePath etc.).
#pragma warning disable CS0436 // type conflicts with imported type

using System.Collections.Concurrent;

namespace Microsoft.Win32
{
	public enum RegistryValueKind
	{
		Unknown = 0, String = 1, ExpandString = 2, Binary = 3, DWord = 4, MultiString = 7, QWord = 11, None = -1
	}

	public enum RegistryHive
	{
		ClassesRoot = unchecked((int)0x80000000), CurrentUser = unchecked((int)0x80000001), LocalMachine = unchecked((int)0x80000002),
		Users = unchecked((int)0x80000003), CurrentConfig = unchecked((int)0x80000005)
	}

	public enum RegistryView
	{
		Default = 0, Registry64 = 0x100, Registry32 = 0x200
	}

	public static class Registry
	{
		private static readonly RegistryKey _classesRoot = new("HKEY_CLASSES_ROOT");
		private static readonly RegistryKey _currentUser = new("HKEY_CURRENT_USER");
		private static readonly RegistryKey _localMachine = new("HKEY_LOCAL_MACHINE");

		public static RegistryKey ClassesRoot => _classesRoot;
		public static RegistryKey CurrentUser => _currentUser;
		public static RegistryKey LocalMachine => _localMachine;

		public static object? GetValue(string keyName, string? valueName, object? defaultValue)
		{
			var root = keyName.Split('\\', 2);
			var rootKey = root[0] switch
			{
				"HKEY_CLASSES_ROOT" => _classesRoot,
				"HKEY_CURRENT_USER" => _currentUser,
				"HKEY_LOCAL_MACHINE" => _localMachine,
				_ => null
			};
			if (rootKey is null)
				return defaultValue;

			using var key = root.Length > 1 ? rootKey.OpenSubKey(root[1]) : rootKey;
			return key?.GetValue(valueName, defaultValue) ?? defaultValue;
		}
	}

	public sealed class RegistryKey : IDisposable
	{
		private readonly ConcurrentDictionary<string, RegistryKey> _subKeys = new(StringComparer.OrdinalIgnoreCase);
		private readonly ConcurrentDictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
		private readonly ConcurrentDictionary<string, RegistryValueKind> _kinds = new(StringComparer.OrdinalIgnoreCase);

		internal RegistryKey(string name) => Name = name;

		public string Name { get; }

		public int ValueCount => _values.Count;

		public int SubKeyCount => _subKeys.Count;

		public static RegistryKey OpenBaseKey(RegistryHive hive, RegistryView view)
			=> hive switch
			{
				RegistryHive.ClassesRoot => Registry.ClassesRoot,
				RegistryHive.CurrentUser => Registry.CurrentUser,
				_ => Registry.LocalMachine
			};

		private static string[] Split(string path)
			=> path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);

		public RegistryKey? OpenSubKey(string name) => OpenSubKey(name, false);

		public RegistryKey? OpenSubKey(string name, bool writable)
		{
			var current = this;
			foreach (var part in Split(name))
			{
				if (!current._subKeys.TryGetValue(part, out var next))
					return null;
				current = next;
			}
			return current;
		}

		public RegistryKey CreateSubKey(string name)
		{
			var current = this;
			foreach (var part in Split(name))
			{
				var parent = current;
				current = parent._subKeys.GetOrAdd(part, p => new RegistryKey($"{parent.Name}\\{p}"));
			}
			return current;
		}

		public string[] GetSubKeyNames() => _subKeys.Keys.ToArray();

		public string[] GetValueNames() => _values.Keys.ToArray();

		public object? GetValue(string? name) => GetValue(name, null);

		public object? GetValue(string? name, object? defaultValue)
			=> _values.TryGetValue(name ?? string.Empty, out var v) ? v : defaultValue;

		public RegistryValueKind GetValueKind(string? name)
			=> _kinds.TryGetValue(name ?? string.Empty, out var k) ? k : throw new System.IO.IOException("The specified registry value does not exist.");

		public void SetValue(string? name, object value) => SetValue(name, value, RegistryValueKind.Unknown);

		public void SetValue(string? name, object value, RegistryValueKind kind)
		{
			_values[name ?? string.Empty] = value;
			_kinds[name ?? string.Empty] = kind;
		}

		public void DeleteValue(string name) => DeleteValue(name, true);

		public void DeleteValue(string name, bool throwOnMissingValue)
		{
			_values.TryRemove(name, out _);
			_kinds.TryRemove(name, out _);
		}

		public void DeleteSubKeyTree(string name) => DeleteSubKeyTree(name, true);

		public void DeleteSubKeyTree(string name, bool throwOnMissingSubKey)
		{
			var parts = Split(name);
			if (parts.Length == 0)
				return;
			var parent = parts.Length == 1 ? this : OpenSubKey(string.Join('\\', parts[..^1]));
			parent?._subKeys.TryRemove(parts[^1], out _);
		}

		public void Close() { }

		public void Dispose() { }

		public override string ToString() => Name;
	}
}
