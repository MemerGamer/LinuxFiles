// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Abstractions
{
	/// <summary>
	/// Provides the per-user directories and file paths the app uses to persist its own data.
	/// </summary>
	public interface IAppDataPaths
	{
		/// <summary>
		/// Gets the directory for user configuration.
		/// </summary>
		string ConfigDirectory { get; }

		/// <summary>
		/// Gets the directory for persistent app data such as databases.
		/// </summary>
		string DataDirectory { get; }

		/// <summary>
		/// Gets the directory for regenerable cache data such as thumbnails.
		/// </summary>
		string CacheDirectory { get; }

		/// <summary>
		/// Gets the directory for state data that is not configuration.
		/// </summary>
		string StateDirectory { get; }

		/// <summary>
		/// Gets the directory for log files.
		/// </summary>
		string LogDirectory { get; }

		/// <summary>
		/// Gets the directory for temporary files.
		/// </summary>
		string TempDirectory { get; }

		/// <summary>
		/// Gets the directory that holds the settings files (user settings, file tags, layout preferences database).
		/// </summary>
		string SettingsDirectory { get; }

		/// <summary>
		/// Gets the path of the user settings JSON file.
		/// </summary>
		string UserSettingsFilePath { get; }

		/// <summary>
		/// Gets the path of the file tags JSON file.
		/// </summary>
		string FileTagsSettingsFilePath { get; }

		/// <summary>
		/// Gets the path of the file backing <see cref="ILocalSettingsStore"/>.
		/// </summary>
		string LocalSettingsFilePath { get; }

		/// <summary>
		/// Creates the config, data, cache, state, log and settings directories if they do not exist.
		/// </summary>
		void EnsureDirectoriesExist();
	}
}
