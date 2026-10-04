// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System;
using Files.Platform.Abstractions;

namespace Files.Platform.Linux
{
	/// <summary>
	/// Resolves app directories following the XDG Base Directory specification.
	/// </summary>
	public sealed class LinuxAppDataPaths : IAppDataPaths
	{
		/// <summary>
		/// The folder name used under each XDG base directory.
		/// </summary>
		public const string AppFolderName = "files";

		private const string SettingsFolderName = "settings";

		/// <summary>
		/// Initializes a new instance using the process environment.
		/// </summary>
		public LinuxAppDataPaths()
			: this(Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
		{
		}

		/// <summary>
		/// Initializes a new instance with injected environment access.
		/// </summary>
		public LinuxAppDataPaths(Func<string, string?> getEnvironmentVariable, string home)
		{
			ConfigDirectory = Path.Combine(XdgBase(getEnvironmentVariable, "XDG_CONFIG_HOME", home, ".config"), AppFolderName);
			DataDirectory = Path.Combine(XdgBase(getEnvironmentVariable, "XDG_DATA_HOME", home, ".local/share"), AppFolderName);
			CacheDirectory = Path.Combine(XdgBase(getEnvironmentVariable, "XDG_CACHE_HOME", home, ".cache"), AppFolderName);
			StateDirectory = Path.Combine(XdgBase(getEnvironmentVariable, "XDG_STATE_HOME", home, ".local/state"), AppFolderName);
			LogDirectory = Path.Combine(StateDirectory, "logs");

			var tmp = getEnvironmentVariable("TMPDIR");
			TempDirectory = !string.IsNullOrEmpty(tmp) && Path.IsPathRooted(tmp) ? tmp : "/tmp";

			SettingsDirectory = Path.Combine(ConfigDirectory, SettingsFolderName);
			UserSettingsFilePath = Path.Combine(SettingsDirectory, "user_settings.json");
			FileTagsSettingsFilePath = Path.Combine(SettingsDirectory, "filetags.json");
			LocalSettingsFilePath = Path.Combine(ConfigDirectory, "local_settings.json");
		}

		/// <inheritdoc/>
		public string ConfigDirectory { get; }

		/// <inheritdoc/>
		public string DataDirectory { get; }

		/// <inheritdoc/>
		public string CacheDirectory { get; }

		/// <inheritdoc/>
		public string StateDirectory { get; }

		/// <inheritdoc/>
		public string LogDirectory { get; }

		/// <inheritdoc/>
		public string TempDirectory { get; }

		/// <inheritdoc/>
		public string SettingsDirectory { get; }

		/// <inheritdoc/>
		public string UserSettingsFilePath { get; }

		/// <inheritdoc/>
		public string FileTagsSettingsFilePath { get; }

		/// <inheritdoc/>
		public string LocalSettingsFilePath { get; }

		/// <inheritdoc/>
		public void EnsureDirectoriesExist()
		{
			Directory.CreateDirectory(ConfigDirectory);
			Directory.CreateDirectory(DataDirectory);
			Directory.CreateDirectory(CacheDirectory);
			Directory.CreateDirectory(StateDirectory);
			Directory.CreateDirectory(LogDirectory);
			Directory.CreateDirectory(SettingsDirectory);
		}

		private static string XdgBase(Func<string, string?> getEnv, string variable, string home, string relativeDefault)
		{
			// The spec requires ignoring relative values.
			var value = getEnv(variable);
			return !string.IsNullOrEmpty(value) && Path.IsPathRooted(value)
				? value
				: Path.Combine(home, relativeDefault);
		}
	}
}
