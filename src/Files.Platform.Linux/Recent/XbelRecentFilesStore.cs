// Copyright (c) Files Community
// Licensed under the MIT License.

#pragma warning disable CA1416

using Files.Platform.Abstractions.Recent;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Files.Platform.Linux.Recent
{
	/// <summary>
	/// Reads and writes <c>$XDG_DATA_HOME/recently-used.xbel</c>, the list GTK and Qt applications share.
	/// Entries written by other applications are preserved. Writes are atomic (temporary file + rename) and the list is capped.
	/// </summary>
	public sealed class XbelRecentFilesStore : IRecentFilesStore, IDisposable
	{
		private static readonly XNamespace Bookmark = "http://www.freedesktop.org/standards/desktop-bookmarks";
		private static readonly XNamespace MimeNs = "http://www.freedesktop.org/standards/shared-mime-info";
		private const string AppName = "Files";

		private readonly object gate = new();
		private readonly string filePath;
		private readonly int maxEntries;
		private FileSystemWatcher? watcher;

		/// <summary>
		/// Creates the store for <paramref name="dataHome"/>, capping the list at <paramref name="maxEntries"/> entries.
		/// </summary>
		public XbelRecentFilesStore(string dataHome, int maxEntries = 500)
		{
			filePath = Path.Combine(dataHome, "recently-used.xbel");
			this.maxEntries = maxEntries;
		}

		/// <inheritdoc/>
		public event EventHandler? Changed
		{
			add
			{
				changed += value;
				EnsureWatcher();
			}
			remove => changed -= value;
		}

		private EventHandler? changed;

		/// <inheritdoc/>
		public IReadOnlyList<RecentEntry> Read()
		{
			lock (gate)
			{
				var doc = Load();
				return doc.Root!.Elements("bookmark")
					.Select(ToEntry)
					.Where(e => e is not null)
					.Select(e => e!)
					.OrderByDescending(e => e.LastUsedUtc)
					.ToArray();
			}
		}

		/// <inheritdoc/>
		public bool Add(string path, string? mimeType = null)
		{
			if (!Path.IsPathRooted(path))
				return false;

			lock (gate)
			{
				var doc = Load();
				var href = ToHref(path);
				var now = FormatTime(DateTime.UtcNow);

				var bookmark = doc.Root!.Elements("bookmark").FirstOrDefault(b => (string?)b.Attribute("href") == href);
				if (bookmark is null)
				{
					bookmark = new XElement("bookmark", new XAttribute("href", href), new XAttribute("added", now));
					doc.Root.Add(bookmark);
				}

				bookmark.SetAttributeValue("modified", now);
				bookmark.SetAttributeValue("visited", now);

				var metadata = EnsureMetadata(bookmark);

				if (mimeType is not null)
				{
					metadata.Elements(MimeNs + "mime-type").Remove();
					metadata.AddFirst(new XElement(MimeNs + "mime-type", new XAttribute("type", mimeType)));
				}

				var apps = metadata.Element(Bookmark + "applications");
				if (apps is null)
				{
					apps = new XElement(Bookmark + "applications");
					metadata.Add(apps);
				}

				var app = apps.Elements(Bookmark + "application").FirstOrDefault(a => (string?)a.Attribute("name") == AppName);
				var count = app is null ? 0 : int.TryParse((string?)app.Attribute("count"), NumberStyles.None, CultureInfo.InvariantCulture, out var c) ? c : 0;
				app?.Remove();
				apps.Add(new XElement(Bookmark + "application",
					new XAttribute("name", AppName),
					new XAttribute("exec", "'files %u'"),
					new XAttribute("modified", now),
					new XAttribute("count", (count + 1).ToString(CultureInfo.InvariantCulture))));

				Trim(doc);
				return Save(doc);
			}
		}

		/// <inheritdoc/>
		public bool Remove(string path)
		{
			lock (gate)
			{
				var doc = Load();
				var href = ToHref(path);
				var matches = doc.Root!.Elements("bookmark").Where(b => (string?)b.Attribute("href") == href).ToList();
				if (matches.Count == 0)
					return true;

				foreach (var match in matches)
					match.Remove();

				return Save(doc);
			}
		}

		/// <inheritdoc/>
		public bool Clear()
		{
			lock (gate)
			{
				var doc = Load();
				doc.Root!.Elements("bookmark").Remove();
				return Save(doc);
			}
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			watcher?.Dispose();
			watcher = null;
		}

		private void Trim(XDocument doc)
		{
			var all = doc.Root!.Elements("bookmark")
				.OrderByDescending(b => ParseTime((string?)b.Attribute("modified")))
				.ToList();

			foreach (var extra in all.Skip(maxEntries))
				extra.Remove();
		}

		private static XElement EnsureMetadata(XElement bookmark)
		{
			var info = bookmark.Element("info");
			if (info is null)
			{
				info = new XElement("info");
				bookmark.Add(info);
			}

			var metadata = info.Elements("metadata").FirstOrDefault(m => (string?)m.Attribute("owner") == "http://freedesktop.org");
			if (metadata is null)
			{
				metadata = new XElement("metadata", new XAttribute("owner", "http://freedesktop.org"));
				info.Add(metadata);
			}

			return metadata;
		}

		private static RecentEntry? ToEntry(XElement bookmark)
		{
			var href = (string?)bookmark.Attribute("href");
			if (href is null || !Uri.TryCreate(href, UriKind.Absolute, out var uri) || !uri.IsFile)
				return null;

			var time = ParseTime((string?)bookmark.Attribute("visited") ?? (string?)bookmark.Attribute("modified") ?? (string?)bookmark.Attribute("added"));
			var mime = bookmark.Element("info")?.Element("metadata")?.Element(MimeNs + "mime-type")?.Attribute("type")?.Value;
			return new RecentEntry(uri.LocalPath, time, mime);
		}

		private static string ToHref(string path) => new Uri(path).AbsoluteUri;

		private static string FormatTime(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

		private static DateTime ParseTime(string? value)
			=> DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.MinValue;

		private XDocument Load()
		{
			try
			{
				if (File.Exists(filePath))
				{
					var doc = XDocument.Load(filePath);
					if (doc.Root is { Name.LocalName: "xbel" })
						return doc;
				}
			}
			catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
			{
				// Corrupt or unreadable: start with an empty list (the next write replaces it)
			}

			return new XDocument(
				new XDeclaration("1.0", "UTF-8", null),
				new XElement("xbel",
					new XAttribute("version", "1.0"),
					new XAttribute(XNamespace.Xmlns + "bookmark", Bookmark.NamespaceName),
					new XAttribute(XNamespace.Xmlns + "mime", MimeNs.NamespaceName)));
		}

		private bool Save(XDocument doc)
		{
			string? temp = null;
			try
			{
				var directory = Path.GetDirectoryName(filePath)!;
				Directory.CreateDirectory(directory);
				temp = Path.Combine(directory, $".recently-used.xbel.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");

				using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true, Encoding = new System.Text.UTF8Encoding(false) }))
				{
					doc.Save(writer);
				}

				File.Move(temp, filePath, overwrite: true);
				temp = null;
				return true;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return false;
			}
			finally
			{
				if (temp is not null)
				{
					try { File.Delete(temp); } catch (IOException) { }
				}
			}
		}

		private void EnsureWatcher()
		{
			lock (gate)
			{
				if (watcher is not null)
					return;

				try
				{
					var directory = Path.GetDirectoryName(filePath)!;
					Directory.CreateDirectory(directory);
					watcher = new FileSystemWatcher(directory, "recently-used.xbel")
					{
						NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
					};
					watcher.Changed += (_, _) => changed?.Invoke(this, EventArgs.Empty);
					watcher.Created += (_, _) => changed?.Invoke(this, EventArgs.Empty);
					watcher.Renamed += (_, _) => changed?.Invoke(this, EventArgs.Empty);
					watcher.EnableRaisingEvents = true;
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
				{
					watcher = null;
				}
			}
		}
	}
}
