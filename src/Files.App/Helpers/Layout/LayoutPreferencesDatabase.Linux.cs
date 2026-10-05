// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions;
using Files.Platform.Abstractions.FileStat;
using Microsoft.Extensions.Logging;
using System.IO;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Files.App.Helpers
{
	/// <summary>
	/// Linux layout preferences database: a JSON file in the app data directory. Entries are keyed by path; the folder's
	/// <c>dev:ino</c> identity (the FRN replacement) is recorded on first use, so preferences saved under an old path are found
	/// and migrated to the new path when the folder was renamed or moved. The <c>frn</c> arguments are unused.
	/// </summary>
	public sealed class LayoutPreferencesDatabase
	{
		private readonly object _gate = new();
		private List<LayoutPreferencesFileEntry>? _entries;

		private static string DatabasePath
			=> Path.Combine(Ioc.Default.GetRequiredService<IAppDataPaths>().DataDirectory, "layoutpreferences.json");

		public LayoutPreferencesItem? GetPreferences(string filePath, ulong? frn)
		{
			lock (_gate)
				return Find(Load(), filePath, true)?.Preferences;
		}

		public void SetPreferences(string filePath, ulong? frn, LayoutPreferencesItem? preferencesItem)
		{
			lock (_gate)
			{
				var entries = Load();
				var existing = Find(entries, filePath, false);

				if (preferencesItem is null)
				{
					if (existing is not null)
						entries.Remove(existing);
				}
				else if (existing is null)
				{
					entries.Add(new LayoutPreferencesFileEntry { FilePath = filePath, FileId = GetId(filePath), Preferences = preferencesItem });
				}
				else
				{
					existing.Preferences = preferencesItem;
					existing.FileId = GetId(filePath) ?? existing.FileId;
				}

				Save(entries);
			}
		}

		public void ResetAll()
		{
			lock (_gate)
				Save([]);
		}

		public void Import(string json)
		{
			var items = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.LayoutPreferencesDatabaseItemArray);

			lock (_gate)
			{
				var entries = new List<LayoutPreferencesFileEntry>();
				foreach (var item in items ?? [])
				{
					if (!string.IsNullOrEmpty(item.FilePath))
						entries.Add(new LayoutPreferencesFileEntry { FilePath = item.FilePath, FileId = GetId(item.FilePath), Preferences = item.LayoutPreferencesManager });
				}

				Save(entries);
			}
		}

		public string Export()
		{
			lock (_gate)
			{
				var list = Load().Select(x => new LayoutPreferencesDatabaseItem { FilePath = x.FilePath, LayoutPreferencesManager = x.Preferences }).ToList();
				return JsonSerializer.Serialize(list, AppJsonSerializerContext.Default.ListLayoutPreferencesDatabaseItem);
			}
		}

		// Looks the entry up by path, then by folder identity. With migrate, keeps the stored identity and path up to date.
		private static LayoutPreferencesFileEntry? Find(List<LayoutPreferencesFileEntry> entries, string filePath, bool migrate)
		{
			if (string.IsNullOrEmpty(filePath))
				return null;

			var id = GetId(filePath);
			var byPath = entries.FirstOrDefault(x => string.Equals(x.FilePath, filePath, StringComparison.Ordinal));
			if (byPath is not null)
				return byPath;

			if (id is null)
				return null;

			var byId = entries.FirstOrDefault(x => string.Equals(x.FileId, id, StringComparison.Ordinal));
			if (byId is not null && migrate)
				byId.FilePath = filePath;

			return byId;
		}

		private static string? GetId(string path)
			=> Ioc.Default.GetService<IFileStatService>() is { } stat && stat.TryGetFileId(path, out var id) ? id.ToString() : null;

		private List<LayoutPreferencesFileEntry> Load()
		{
			if (_entries is not null)
				return _entries;

			var entries = new List<LayoutPreferencesFileEntry>();
			try
			{
				var path = DatabasePath;
				if (File.Exists(path))
					entries = JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonSerializerContext.Default.ListLayoutPreferencesFileEntry) ?? entries;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
			{
				App.Logger?.LogWarning(ex, "Could not read the layout preferences database; starting empty.");
			}

			return _entries = entries;
		}

		private void Save(List<LayoutPreferencesFileEntry> entries)
		{
			_entries = entries;

			string? temp = null;
			try
			{
				var path = DatabasePath;
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
				File.WriteAllText(temp, JsonSerializer.Serialize(entries, AppJsonSerializerContext.Default.ListLayoutPreferencesFileEntry));
				File.Move(temp, path, overwrite: true);
				temp = null;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				App.Logger?.LogWarning(ex, "Could not save the layout preferences database.");
			}
			finally
			{
				if (temp is not null)
					SafetyExtensions.IgnoreExceptions(() => File.Delete(temp));
			}
		}
	}
}
