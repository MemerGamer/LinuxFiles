// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Files.Platform.Linux.Trash
{
	/// <summary>
	/// Maintains the <c>directorysizes</c> cache of one trash folder. Each line is
	/// <c>size mtime percent-encoded-name</c> where <c>mtime</c> is the modification time (Unix seconds) of the matching <c>.trashinfo</c> file.
	/// </summary>
	internal sealed class DirectorySizesCache
	{
		private static readonly object Gate = new();

		private readonly string _path;
		private readonly DirectoryHandle? _directory;

		public DirectorySizesCache(string trashRoot, DirectoryHandle? directory = null)
		{
			_path = Path.Combine(trashRoot, "directorysizes");
			_directory = directory;
		}

		public bool TryGet(string name, long infoMtime, out long size)
		{
			lock (Gate)
			{
				if (Read().TryGetValue(name, out var entry) && entry.Mtime == infoMtime)
				{
					size = entry.Size;
					return true;
				}
			}

			size = 0;
			return false;
		}

		public void Set(string name, long size, long infoMtime)
		{
			lock (Gate)
			{
				var entries = Read();
				entries[name] = (size, infoMtime);
				Write(entries);
			}
		}

		public void Remove(IEnumerable<string> names)
		{
			lock (Gate)
			{
				var entries = Read();
				var changed = false;
				foreach (var name in names)
					changed |= entries.Remove(name);

				if (changed)
					Write(entries);
			}
		}

		public void Clear()
		{
			lock (Gate)
			{
				try
				{
					File.Delete(_path);
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
		}

		private Dictionary<string, (long Size, long Mtime)> Read()
		{
			var entries = new Dictionary<string, (long, long)>(StringComparer.Ordinal);

			try
			{
				if (_directory is null && !File.Exists(_path))
					return entries;

				using var stream = _directory is null ? File.OpenRead(_path) : PosixNative.OpenFileAt(_directory.Descriptor, "directorysizes", _path, false);
				using var reader = new StreamReader(stream);
				while (reader.ReadLine() is { } line)
				{
					var parts = line.Split(' ', 3);
					if (parts.Length == 3 &&
						long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var size) &&
						long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var mtime) &&
						TrashInfoFile.TryDecodePath(parts[2], out var name))
					{
						entries[name] = (size, mtime);
					}
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}

			return entries;
		}

		private void Write(Dictionary<string, (long Size, long Mtime)> entries)
		{
			try
			{
				var builder = new StringBuilder();
				foreach (var (name, entry) in entries)
				{
					builder.Append(entry.Size.ToString(CultureInfo.InvariantCulture)).Append(' ')
						.Append(entry.Mtime.ToString(CultureInfo.InvariantCulture)).Append(' ')
						.Append(TrashInfoFile.EncodePath(name)).Append('\n');
				}

				var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
				if (_directory is null)
				{
					File.WriteAllText(temp, builder.ToString(), new UTF8Encoding(false));
					File.Move(temp, _path, overwrite: true);
				}
				else
				{
					var name = Path.GetFileName(temp);
					var created = false;
					try
					{
						using (var output = PosixNative.OpenFileAt(_directory.Descriptor, name, temp, true))
						{
							created = true;
							using var writer = new StreamWriter(output, new UTF8Encoding(false));
							writer.Write(builder.ToString());
						}
						PosixNative.RenameAt(_directory.Descriptor, name, _directory.Descriptor, "directorysizes", true, _path);
					}
					finally
					{
						if (created)
						{
							try
							{
								PosixNative.UnlinkAt(_directory.Descriptor, name, 0, temp);
							}
							catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
							{
							}
						}
					}
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}
}
