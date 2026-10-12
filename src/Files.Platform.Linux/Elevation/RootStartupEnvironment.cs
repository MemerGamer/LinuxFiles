// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Native;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Files.Platform.Linux.Elevation
{
	/// <summary>Redirects root's settings before Uno or application services can use inherited user directories.</summary>
	public static class RootStartupEnvironment
	{
		public static void Apply()
		{
			if (ProcessIdentityNative.CurrentUserId != 0) return;
			var home = ElevationNative.HomeDirectory(0);
			var overrides = GetOverrides(home);
			var inspector = new StatxFileOwnershipInspector();
			ValidateDirectory(home, inspector, allowMissing: false);
			foreach (var variable in new[] { "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME", "XDG_STATE_HOME" })
				ValidateDirectory(overrides[variable]!, inspector, allowMissing: true);
			foreach (var entry in overrides)
				Environment.SetEnvironmentVariable(entry.Key, entry.Value);
		}

		/// <summary>Keep only root storage, display connection and locale settings for child processes.</summary>
		public static void ApplyChildEnvironment(ProcessStartInfo info, uint effectiveUserId, string rootHome)
		{
			if (effectiveUserId != 0) return;
			var overrides = GetOverrides(rootHome);
			var kept = new Dictionary<string, string?>();
			foreach (var name in new[] { "DISPLAY", "XAUTHORITY", "LANG", "LANGUAGE", "LC_ALL",
				"LC_CTYPE", "LC_NUMERIC", "LC_TIME", "LC_COLLATE", "LC_MONETARY", "LC_MESSAGES",
				"LC_PAPER", "LC_NAME", "LC_ADDRESS", "LC_TELEPHONE", "LC_MEASUREMENT", "LC_IDENTIFICATION" })
			{
				if (info.Environment.TryGetValue(name, out var value)) kept[name] = value;
			}
			info.Environment.Clear();
			foreach (var entry in kept) info.Environment[entry.Key] = entry.Value;
			foreach (var entry in overrides)
				if (entry.Value is not null) info.Environment[entry.Key] = entry.Value;
		}

		public static IReadOnlyDictionary<string, string?> GetOverrides(string rootHome)
		{
			if (!Path.IsPathFullyQualified(rootHome) || rootHome == "/" || rootHome.Contains('\0'))
				throw new IOException("Root account home is invalid.");

			return new Dictionary<string, string?>
			{
				["HOME"] = rootHome,
				["XDG_CONFIG_HOME"] = Path.Combine(rootHome, ".config"),
				["XDG_DATA_HOME"] = Path.Combine(rootHome, ".local/share"),
				["XDG_CACHE_HOME"] = Path.Combine(rootHome, ".cache"),
				["XDG_STATE_HOME"] = Path.Combine(rootHome, ".local/state"),
				["XDG_DATA_DIRS"] = "/usr/local/share:/usr/share",
				["XDG_CONFIG_DIRS"] = "/etc/xdg",
				["PATH"] = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",
				["TERMINAL"] = null,
				["EDITOR"] = null,
				["VISUAL"] = null,
				["BROWSER"] = null,
				["SHELL"] = null,
				["XDG_CURRENT_DESKTOP"] = null,
				["XDG_RUNTIME_DIR"] = null,
				["DBUS_SESSION_BUS_ADDRESS"] = null,
				["FILES_GVFS_DIR"] = null,
				["TMPDIR"] = null,
			};
		}

		public static void ValidateDirectory(string directory, IFileOwnershipInspector inspector, bool allowMissing)
		{
			var path = "/";
			foreach (var component in Path.GetFullPath(directory).Split('/', StringSplitOptions.RemoveEmptyEntries))
			{
				path = Path.Combine(path, component);
				if (!inspector.TryGetInfo(path, out var info))
				{
					if (allowMissing && !File.Exists(path) && !Directory.Exists(path)) return;
					throw new IOException("Root settings directory cannot be inspected.");
				}
				if (!info.IsDirectory || info.IsSymbolicLink || info.OwnerUserId != 0 ||
					(info.Mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
					throw new IOException("Root settings directory is not exclusively controlled by root.");
			}
		}
	}
}
