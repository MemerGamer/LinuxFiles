// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Appearance;
using Files.Platform.Linux.Theme;
using Files.Platform.Linux.Windowing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Files.Platform.Tests.Theme
{
	[TestClass]
	public sealed class AppearanceTests
	{
		[TestMethod]
		public void Support_DoesNotMistakeAtomsOrCompositorPresenceForProtocolSupport()
		{
			Assert.AreEqual(OpacitySupport.Unknown, X11AppearanceSupport.Detect("unknown", "", true, true).WindowOpacity);
			Assert.AreEqual(OpacitySupport.NoCompositor, X11AppearanceSupport.Detect("KWin", "KDE", false, true).WindowOpacity);
			Assert.AreEqual(OpacitySupport.Satellite, X11AppearanceSupport.Detect("KWin", "niri", true, true).WindowOpacity);
			Assert.AreEqual(OpacitySupport.Satellite, X11AppearanceSupport.Detect("xwayland-satellite", "", true, true).WindowOpacity);
			Assert.IsFalse(X11AppearanceSupport.Detect("Mutter", "GNOME", true, true).Blur);
		}

		[TestMethod]
		public void Backdrop_RetainsRequestedModeWhileResolvingReadableFallback()
		{
			var kwin = X11AppearanceSupport.Detect("KWin", "KDE", true, true);
			Assert.AreEqual(BackdropMode.Blur, kwin.Resolve(BackdropMode.Blur, false));
			Assert.AreEqual(BackdropMode.Solid, kwin.Resolve(BackdropMode.Blur, true));
			var noBlur = X11AppearanceSupport.Detect("KWin", "KDE", true, false);
			Assert.AreEqual(BackdropMode.Transparent, noBlur.Resolve(BackdropMode.Transparent, false));
			Assert.AreEqual(BackdropMode.Solid, noBlur.Resolve(BackdropMode.Blur, false));
			Assert.AreEqual(BackdropMode.Solid, X11AppearanceSupport.Detect("unknown", "", true, false).Resolve(BackdropMode.Transparent, false));
		}

		[TestMethod]
		public void Migration_PreservesBothOldChoicesAndExplicitNewChoice()
		{
			Assert.AreEqual(ColourSource.Files, AppearancePreferences.MigrateColourSource(null, false));
			Assert.AreEqual(ColourSource.Adwaita, AppearancePreferences.MigrateColourSource("", true));
			Assert.AreEqual(ColourSource.System, AppearancePreferences.MigrateColourSource("System", true));
			Assert.AreEqual(ColourSource.Files, AppearancePreferences.MigrateColourSource("Files", true));
			Assert.AreEqual(ColourSource.Adwaita, AppearancePreferences.MigrateColourSource("999", true));
			Assert.AreEqual(1f, AppearancePreferences.ClampOpacity(float.NaN));
			Assert.AreEqual(1f, AppearancePreferences.ClampOpacity(float.PositiveInfinity));
			Assert.AreEqual(0.2f, AppearancePreferences.ClampOpacity(-1, 0.2f));
		}

		[TestMethod]
		public void Portal_LeavesUnknownAndInvalidPreferencesUnset()
		{
			Assert.IsNull(LinuxSystemAppearanceService.ReadScheme((VariantValue)0u));
			Assert.AreEqual(true, LinuxSystemAppearanceService.ReadScheme((VariantValue)1u));
			Assert.AreEqual(false, LinuxSystemAppearanceService.ReadScheme((VariantValue)2u));
			Assert.IsNull(LinuxSystemAppearanceService.ReadScheme((VariantValue)"dark"));
			Assert.IsNull(LinuxSystemAppearanceService.ReadContrast((VariantValue)2u));
			Assert.AreEqual(true, LinuxSystemAppearanceService.ReadContrast((VariantValue)1u));
			Assert.AreEqual(new AppearanceColor(255, 128, 0), LinuxSystemAppearanceService.ReadAccent(Struct.Create(1d, 0.5d, 0d)));
			Assert.IsNull(LinuxSystemAppearanceService.ReadAccent(Struct.Create(double.NaN, 0d, 0d)));
			Assert.IsNull(LinuxSystemAppearanceService.ReadAccent(Struct.Create(2d, 0d, 0d)));
			Assert.IsNull(LinuxSystemAppearanceService.ReadAccent(Struct.Create(1u, 0u, 0u)));
		}

		[TestMethod]
		public void Css_ResolvesLiteralAliasesAndLastDefinitionWithoutEvaluatingCode()
		{
			var colors = GtkNamedColors.Parse("/* @define-color base #000; */ @import url('evil'); @define-color base #123; @define-color alias @base; @define-color base rgba(255, 0, 128, .5); @define-color expr shade(@base, 0.5); @define-color loop @loop; @define-color bad url(file:///etc/passwd);");
			Assert.AreEqual(new AppearanceColor(255, 0, 128, 128), colors["alias"]);
			Assert.IsFalse(colors.ContainsKey("expr"));
			Assert.IsFalse(colors.ContainsKey("loop"));
			Assert.IsFalse(colors.ContainsKey("bad"));
			Assert.AreEqual(new AppearanceColor(17, 34, 51, 68), GtkNamedColors.ParseLiteral("#1234"));
			Assert.AreEqual(new AppearanceColor(255, 0, 0), GtkNamedColors.ParseLiteral("rgb(100%, 0%, 0%)"));
			Assert.IsNull(GtkNamedColors.ParseLiteral("rgba(0,0,0,NaN)"));
			Assert.AreEqual(0, GtkNamedColors.Parse(new string('x', GtkNamedColors.MaxCssBytes + 1)).Count);
			Assert.IsFalse(GtkNamedColors.IsSafeThemeName("../theme"));
			Assert.IsFalse(GtkNamedColors.IsSafeThemeName(".."));
		}

		[TestMethod]
		public async Task Css_RejectsOversizeFiles()
		{
			var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".css");
			try
			{
				await File.WriteAllTextAsync(path, new string('x', GtkNamedColors.MaxCssBytes + 1));
				Assert.IsNull(await GtkNamedColors.ReadAsync(path));
			}
			finally { File.Delete(path); }
		}

		[TestMethod]
		public void Palette_MapsControlStatesPreservesExplicitThemeAndReadableAccentText()
		{
			var bg = new AppearanceColor(250, 240, 230);
			var named = new Dictionary<string, AppearanceColor> { ["theme_bg_color"] = bg, ["theme_fg_color"] = new(12, 23, 34), ["theme_selected_fg_color"] = new(255, 255, 255) };
			var system = new SystemAppearance(false, false, new(255, 240, 0), named);
			var map = SystemAppearancePalette.Map(system, false);
			Assert.AreEqual(bg, map["App.Theme.BackgroundBrush"]);
			Assert.AreEqual(named["theme_fg_color"], map["ToolbarButtonForegroundPointerOver"]);
			Assert.AreEqual(new AppearanceColor(0, 0, 0), map["TextOnAccentFillColorPrimaryBrush"]);
			Assert.IsFalse(SystemAppearancePalette.Map(system, true).ContainsKey("App.Theme.BackgroundBrush"));
			Assert.IsTrue(SystemAppearancePalette.Map(system, true).ContainsKey("SystemAccentColor"));
			Assert.IsTrue(SystemAppearancePalette.ContrastRatio(map["SystemAccentColor"], map["TextOnAccentFillColorPrimaryBrush"]) >= 4.5);
		}
	}
}
