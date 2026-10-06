// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Files.Platform.Tests.Localization
{
	/// <summary>
	/// New strings are added to en-US only; the other locales fall back to it at runtime. These checks make sure code never
	/// asks for a key that en-US does not define (which would show an empty label in every locale).
	/// </summary>
	[TestClass]
	public sealed partial class ResourceKeyTests
	{
		[GeneratedRegex(@"""([A-Za-z0-9_.]+)""\s*\.GetLocalizedResource\(")]
		private static partial Regex LiteralKeyRegex();

		[GeneratedRegex(@"ResourceString\s+(?:Key=)?([A-Za-z0-9_./]+)")]
		private static partial Regex XamlKeyRegex();

		[GeneratedRegex(@"\bx:Uid=""([^""]+)""")]
		private static partial Regex UidRegex();

		private static string AppDir
		{
			get
			{
				var dir = new DirectoryInfo(AppContext.BaseDirectory);
				while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Files.slnx")))
					dir = dir.Parent;

				Assert.IsNotNull(dir, "Repository root not found");
				return Path.Combine(dir.FullName, "src", "Files.App");
			}
		}

		private static HashSet<string> LoadKeys(string locale)
		{
			var doc = XDocument.Load(Path.Combine(AppDir, "Strings", locale, "Resources.resw"));
			return doc.Descendants("data").Select(d => (string)d.Attribute("name")!).ToHashSet(StringComparer.OrdinalIgnoreCase);
		}

		private static IEnumerable<string> SourceFiles(string pattern) =>
			Directory.EnumerateFiles(AppDir, pattern, SearchOption.AllDirectories)
				.Where(f => !f.EndsWith(".Windows.cs", StringComparison.Ordinal) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

		[TestMethod]
		public void LiteralKeysUsedInCode_ExistInEnUs()
		{
			var keys = LoadKeys("en-US");
			var missing = SourceFiles("*.cs")
				.SelectMany(f => LiteralKeyRegex().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
				.Where(x => !keys.Contains(x.Key))
				.Select(x => $"{x.File}: {x.Key}")
				.Distinct()
				.ToList();

			Assert.AreEqual(0, missing.Count, "Keys missing from en-US: " + string.Join(", ", missing));
		}

		[TestMethod]
		public void XamlKeys_ExistInEnUs()
		{
			var keys = LoadKeys("en-US");
			var missing = new List<string>();
			foreach (var file in SourceFiles("*.xaml"))
			{
				var text = File.ReadAllText(file);
				foreach (Match m in XamlKeyRegex().Matches(text))
					if (!keys.Contains(m.Groups[1].Value.Replace('/', '.')))
						missing.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");

				foreach (Match m in UidRegex().Matches(text))
					if (!keys.Any(k => k.StartsWith(m.Groups[1].Value + ".", StringComparison.OrdinalIgnoreCase)))
						missing.Add($"{Path.GetFileName(file)}: x:Uid {m.Groups[1].Value}");
			}

			Assert.AreEqual(0, missing.Count, "Keys missing from en-US: " + string.Join(", ", missing.Distinct()));
		}

		[TestMethod]
		public void OtherLocales_DoNotDefineEmptyValues()
		{
			var offenders = new List<string>();
			foreach (var dir in Directory.EnumerateDirectories(Path.Combine(AppDir, "Strings")))
			{
				var doc = XDocument.Load(Path.Combine(dir, "Resources.resw"));
				offenders.AddRange(doc.Descendants("data")
					.Where(d => string.IsNullOrWhiteSpace((string?)d.Element("value")))
					.Select(d => $"{Path.GetFileName(dir)}: {(string)d.Attribute("name")!}"));
			}

			Assert.AreEqual(0, offenders.Count, "Empty translations hide the en-US fallback: " + string.Join(", ", offenders.Take(20)));
		}
	}
}
