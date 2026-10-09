// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Windowing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace Files.Platform.Tests.Windowing
{
	[TestClass]
	public sealed class CursorSettingsResolverTests
	{
		[TestMethod]
		public void ExplicitEnvironment_DoesNotReadFallbacks()
		{
			var result = CursorSettingsResolver.Resolve(_ => "explicit", ("resource", "48"),
				_ => throw new InvalidOperationException("Unexpected gsettings read"));
			Assert.IsNull(result.Theme);
			Assert.IsNull(result.Size);
		}

		[TestMethod]
		public void XResources_TakePrecedenceOverGSettings()
		{
			var result = CursorSettingsResolver.Resolve(_ => null, ("Breeze", " 048 "),
				_ => throw new InvalidOperationException("Unexpected gsettings read"));
			Assert.AreEqual("Breeze", result.Theme);
			Assert.AreEqual("48", result.Size);
		}

		[TestMethod]
		public void MissingSettings_FallBackIndependently()
		{
			var reads = new List<string>();
			var result = CursorSettingsResolver.Resolve(key => key == "XCURSOR_THEME" ? "Custom" : null,
				(null, null), key => { reads.Add(key); return "32\n"; });
			Assert.IsNull(result.Theme);
			Assert.AreEqual("32", result.Size);
			CollectionAssert.AreEqual(new[] { "cursor-size" }, reads);

			reads.Clear();
			result = CursorSettingsResolver.Resolve(_ => null, (null, "24"),
				key => { reads.Add(key); return "'Adwaita'\n"; });
			Assert.AreEqual("Adwaita", result.Theme);
			Assert.AreEqual("24", result.Size);
			CollectionAssert.AreEqual(new[] { "cursor-theme" }, reads);
		}

		[TestMethod]
		public void EmptyEnvironmentAndResources_UseGSettings()
		{
			var result = CursorSettingsResolver.Resolve(_ => "", ("", ""),
				key => key == "cursor-theme" ? "'Adwaita'" : "24");
			Assert.AreEqual("Adwaita", result.Theme);
			Assert.AreEqual("24", result.Size);
		}

		[TestMethod]
		public void MissingGSettings_LeaveDefaultsAlone()
		{
			var result = CursorSettingsResolver.Resolve(_ => null, (null, null), _ => null);
			Assert.IsNull(result.Theme);
			Assert.IsNull(result.Size);
		}

		[TestMethod]
		public void GSettingsTheme_DecodesQuotedStrings()
		{
			foreach (var output in new[] { "'Breeze Snow'", "\"Breeze Snow\"\n" })
				Assert.AreEqual("Breeze Snow", CursorSettingsResolver.Resolve(_ => null, (null, "24"), _ => output).Theme);
			Assert.AreEqual("Owner's theme", CursorSettingsResolver.Resolve(_ => null, (null, "24"),
				_ => "'Owner\\'s theme'").Theme);
		}

		[TestMethod]
		public void MalformedGSettingsTheme_IsIgnored()
		{
			foreach (var output in new[] { "", "''", "Adwaita", "'Adwaita", "'a'b'", "'a\\'", "'a\\nb'", "'../theme'", "'a\0b'" })
				Assert.IsNull(CursorSettingsResolver.Resolve(_ => null, (null, "24"), _ => output).Theme, output);
		}

		[TestMethod]
		public void InvalidResourceTheme_DoesNotOverrideWithGSettings()
		{
			foreach (var theme in new[] { "..", "/tmp/theme", "a\\b", "a\nb", new string('a', 257) })
				Assert.IsNull(CursorSettingsResolver.Resolve(_ => null, (theme, "24"),
					_ => throw new InvalidOperationException("Unexpected gsettings read")).Theme, theme);
		}

		[TestMethod]
		public void InvalidSizes_AreIgnored()
		{
			foreach (var size in new[] { "0", "-1", "1025", "2147483648", "NaN", "24.5", "abc", "24\0" })
			{
				Assert.IsNull(CursorSettingsResolver.Resolve(_ => null, ("Adwaita", size),
					_ => throw new InvalidOperationException("Unexpected gsettings read")).Size, size);
				Assert.IsNull(CursorSettingsResolver.Resolve(_ => null, ("Adwaita", null), _ => size).Size, size);
			}
		}
	}
}
