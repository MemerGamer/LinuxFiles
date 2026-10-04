// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Microsoft.Extensions.Logging;
using System.IO;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Files.App.Utils.FileTags
{
	/// <summary>
	/// Linux tag database: a JSON file in the app data directory mapping paths to tag UIDs. It is the fallback (and the source of
	/// truth for tags the xattr store cannot hold); on file systems with xattr support tags are also written to <c>user.xdg.tags</c>.
	/// File reference numbers do not exist on Linux, so entries are keyed by path only and the FRN arguments are ignored.
	/// </summary>
	public sealed partial class FileTagsDatabase
	{
		private readonly object _gate = new();
		private Dictionary<string, TaggedFile>? _entries;

		private static string DatabasePath
			=> Path.Combine(Ioc.Default.GetRequiredService<Files.Platform.Abstractions.IAppDataPaths>().DataDirectory, "filetags.json");

		public void SetTags(string filePath, ulong? frn, string[] tags)
		{
			lock (_gate)
			{
				var entries = Load();
				if (tags is [])
					entries.Remove(filePath);
				else
					entries[filePath] = new TaggedFile { FilePath = filePath, Tags = tags };

				Save(entries);
			}
		}

		public void UpdateTag(string oldFilePath, ulong? frn, string? newFilePath)
		{
			if (newFilePath is null)
				return;

			lock (_gate)
			{
				var entries = Load();
				if (!entries.Remove(oldFilePath, out var existing))
					return;

				existing.FilePath = newFilePath;
				entries[newFilePath] = existing;
				Save(entries);
			}
		}

		public void UpdateTag(ulong oldFrn, ulong? frn, string? newFilePath)
		{
			// No file reference numbers on Linux
		}

		public string[] GetTags(string? filePath, ulong? frn)
		{
			if (filePath is null)
				return [];

			lock (_gate)
				return Load().TryGetValue(filePath, out var tagged) ? tagged.Tags : [];
		}

		public IEnumerable<TaggedFile> GetAll()
		{
			lock (_gate)
				return Load().Values.ToList();
		}

		public IEnumerable<TaggedFile> GetAllUnderPath(string folderPath)
		{
			var prefix = folderPath.TrimEnd('/') + "/";
			lock (_gate)
				return Load().Values.Where(x => x.FilePath.StartsWith(prefix, StringComparison.Ordinal)).ToList();
		}

		public void Import(string json)
		{
			var tags = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.TaggedFileArray);

			lock (_gate)
			{
				var entries = new Dictionary<string, TaggedFile>(StringComparer.Ordinal);
				foreach (var tag in tags ?? [])
				{
					if (!string.IsNullOrEmpty(tag.FilePath) && tag.Tags.Length > 0)
						entries[tag.FilePath] = tag;
				}

				Save(entries);
			}
		}

		public string Export()
		{
			lock (_gate)
				return JsonSerializer.Serialize(Load().Values.ToList(), AppJsonSerializerContext.Default.ListTaggedFile);
		}

		private Dictionary<string, TaggedFile> Load()
		{
			if (_entries is not null)
				return _entries;

			var entries = new Dictionary<string, TaggedFile>(StringComparer.Ordinal);
			try
			{
				var path = DatabasePath;
				if (File.Exists(path))
				{
					var list = JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonSerializerContext.Default.TaggedFileArray);
					foreach (var tag in list ?? [])
					{
						if (!string.IsNullOrEmpty(tag.FilePath) && tag.Tags.Length > 0)
							entries[tag.FilePath] = tag;
					}
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
			{
				App.Logger?.LogWarning(ex, "Could not read the file tags database; starting empty.");
			}

			return _entries = entries;
		}

		private void Save(Dictionary<string, TaggedFile> entries)
		{
			_entries = entries;

			string? temp = null;
			try
			{
				var path = DatabasePath;
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
				File.WriteAllText(temp, JsonSerializer.Serialize(entries.Values.ToList(), AppJsonSerializerContext.Default.ListTaggedFile));
				File.Move(temp, path, overwrite: true);
				temp = null;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				App.Logger?.LogWarning(ex, "Could not save the file tags database.");
			}
			finally
			{
				if (temp is not null)
					SafetyExtensions.IgnoreExceptions(() => File.Delete(temp));
			}
		}
	}
}
#endif
