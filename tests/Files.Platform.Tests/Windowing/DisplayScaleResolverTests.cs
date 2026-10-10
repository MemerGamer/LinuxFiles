// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Windowing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace Files.Platform.Tests.Windowing
{
	[TestClass]
	public sealed class DisplayScaleResolverTests
	{
		private const string NiriOutputsFixture = """
			{"DP-1":{"name":"DP-1","logical":{"scale":1.25,"x":0,"y":0}},
			 "HDMI-A-1":{"name":"HDMI-A-1","logical":{"scale":2,"x":1920,"y":0}}}
			""";

		private const string NiriFocusedFixture = """
			{"name":"HDMI-A-1","logical":{"scale":2}}
			""";

		private static string? Resolve(string? xResources, params (string Key, string Value)[] env)
		{
			var map = new Dictionary<string, string>();
			foreach (var (key, value) in env)
				map[key] = value;

			return DisplayScaleResolver.Resolve(k => map.GetValueOrDefault(k), xResources);
		}

		[TestMethod]
		public void XftDpi_LeavesDetectionToUno()
		{
			Assert.IsNull(Resolve("Xft.dpi:\t144\n", ("GDK_SCALE", "2")));
		}

		[TestMethod]
		public void ExplicitOverride_IsRespected()
		{
			Assert.IsNull(Resolve(null, ("UNO_DISPLAY_SCALE_OVERRIDE", "1.5"), ("GDK_SCALE", "2")));
		}

		[TestMethod]
		public void GdkScale_UsedWithoutXftDpi()
		{
			Assert.AreEqual("2", Resolve("*.background: black\n", ("GDK_SCALE", "2")));
		}

		[TestMethod]
		public void QtScaleFactor_BeatsGdkScale()
		{
			Assert.AreEqual("1.25", Resolve(null, ("QT_SCALE_FACTOR", "1.25"), ("GDK_SCALE", "2")));
		}

		[TestMethod]
		public void QtScreenFactors_UsesFirstScreen()
		{
			Assert.AreEqual("1.5", Resolve(null, ("QT_SCREEN_SCALE_FACTORS", "DP-1=1.5;HDMI-1=1")));
		}

		[TestMethod]
		public void InvalidOrUnitScale_ReturnsNull()
		{
			Assert.IsNull(Resolve(null, ("GDK_SCALE", "1")));
			Assert.IsNull(Resolve(null, ("QT_SCALE_FACTOR", "abc")));
			Assert.IsNull(Resolve(null, ("QT_SCALE_FACTOR", "40")));
		}

		[TestMethod]
		public void EffectiveScale_PrefersOverrideThenXftDpi()
		{
			var map = new Dictionary<string, string> { ["UNO_DISPLAY_SCALE_OVERRIDE"] = "1.5" };
			Assert.AreEqual(1.5, DisplayScaleResolver.GetEffectiveScale(k => map.GetValueOrDefault(k), "Xft.dpi:\t192\n"));
			Assert.AreEqual(2.0, DisplayScaleResolver.GetEffectiveScale(_ => null, "Xft.dpi:\t192\n"));
			Assert.AreEqual(1.0, DisplayScaleResolver.GetEffectiveScale(_ => null, null));
		}

		[TestMethod]
		public void MalformedScreenFactors_DoNotThrow()
		{
			foreach (var value in new[] { ";", ";;", "=", "DP-1=", "DP-1=abc;", " ; ", "DP-1=0" })
				Assert.IsNull(Resolve(null, ("QT_SCREEN_SCALE_FACTORS", value)), value);
		}

		[TestMethod]
		public void QtFactors_AreComposed()
		{
			Assert.AreEqual("2", Resolve(null, ("QT_SCALE_FACTOR", "1"), ("QT_SCREEN_SCALE_FACTORS", "DP-1=2")));
			Assert.AreEqual("3", Resolve(null, ("QT_SCALE_FACTOR", "1.5"), ("QT_SCREEN_SCALE_FACTORS", "DP-1=2")));
		}

		[TestMethod]
		public void FractionalXftDpi_IsPassedAsOverride()
		{
			Assert.AreEqual("1.25", Resolve("Xft.dpi:\t120.0\n", ("GDK_SCALE", "2")));
			Assert.AreEqual("1.505", Resolve("Xft.dpi:\t144.5\n"));
			Assert.IsNull(Resolve("Xft.dpi:\t96.0\n"));
		}

		[TestMethod]
		public void NonFiniteXftDpi_IsIgnored()
		{
			foreach (var value in new[] { "Infinity", "-Infinity", "NaN" })
			{
				Assert.IsNull(Resolve($"Xft.dpi:\t{value}\n"), value);
				Assert.IsNull(DisplayScaleResolver.ParseXftDpi($"Xft.dpi:\t{value}\n"), value);
				Assert.AreEqual(1.0, DisplayScaleResolver.GetEffectiveScale(_ => null, $"Xft.dpi:\t{value}\n"), value);
			}

			Assert.AreEqual("2", Resolve("Xft.dpi:\tInfinity\n", ("GDK_SCALE", "2")));
		}

		[TestMethod]
		public void XftDpi_ToleratesWhitespaceBeforeColon()
		{
			Assert.IsNull(Resolve("Xft.dpi : 144\n", ("GDK_SCALE", "2")));
			Assert.AreEqual(1.5, DisplayScaleResolver.GetEffectiveScale(_ => null, "  Xft.dpi\t:\t144\r\n"));
		}


		[TestMethod]
		public void NiriOutputs_CliAndSocketFixtures_PreferFocusedOutput()
		{
			Assert.AreEqual(2.0, DisplayScaleResolver.ParseNiriOutputs(NiriOutputsFixture, NiriFocusedFixture));
			Assert.AreEqual(2.0, DisplayScaleResolver.ParseNiriOutputs(
				"{\"Ok\":{\"Outputs\":" + NiriOutputsFixture + "}}",
				"{\"Ok\":{\"FocusedOutput\":" + NiriFocusedFixture + "}}"));
		}

		[TestMethod]
		public void NiriOutputs_UnknownFocus_UsesFirstOutput()
		{
			foreach (var focus in new string?[] { null, "null", "{}", "garbage", "{\"name\":\"missing\"}", "{\"Ok\":{\"FocusedOutput\":null}}" })
				Assert.AreEqual(1.25, DisplayScaleResolver.ParseNiriOutputs(NiriOutputsFixture, focus), focus);
		}

		[TestMethod]
		public void NiriOutputs_MissingFirstScale_CanStillUseFocusedOutput()
		{
			const string outputs = """{"DP-1":{"logical":null},"HDMI-A-1":{"logical":{"scale":2}}}""";
			Assert.IsNull(DisplayScaleResolver.ParseNiriOutputs(outputs));
			Assert.AreEqual(2.0, DisplayScaleResolver.ParseNiriOutputs(outputs, NiriFocusedFixture));
		}

		[TestMethod]
		[DataRow("garbage")]
		[DataRow("[]")]
		[DataRow("null")]
		[DataRow("{}")]
		[DataRow("{\"Err\":\"failed\"}")]
		[DataRow("{\"Ok\":{\"Outputs\":[]}}")]
		[DataRow("{\"Ok\":{\"Version\":\"26.04\"}}")]
		[DataRow("{\"DP-1\":{}}")]
		[DataRow("{\"DP-1\":{\"logical\":null}}")]
		[DataRow("{\"DP-1\":{\"logical\":{}}}")]
		[DataRow("{\"DP-1\":{\"logical\":{\"scale\":\"1.25\"}}}")]
		[DataRow("{\"DP-1\":{\"logical\":{\"scale\":0}}}")]
		[DataRow("{\"DP-1\":{\"logical\":{\"scale\":-1}}}")]
		[DataRow("{\"DP-1\":{\"logical\":{\"scale\":1e999}}}")]
		public void NiriOutputs_MalformedFixtures_ReturnNull(string json)
		{
			Assert.IsNull(DisplayScaleResolver.ParseNiriOutputs(json));
		}

		[TestMethod]
		[DataRow(0.5, 1.0)]
		[DataRow(1.25, 1.25)]
		[DataRow(8.0, 4.0)]
		public void NiriOutputs_ClampsScale(double input, double expected)
		{
			var json = "{\"DP-1\":{\"logical\":{\"scale\":" + input.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}}";
			Assert.AreEqual(expected, DisplayScaleResolver.ParseNiriOutputs(json));
		}

		[TestMethod]
		[DataRow(null, false, false)]
		[DataRow(null, true, true)]
		[DataRow("1", false, true)]
		[DataRow("1", true, true)]
		[DataRow("0", false, false)]
		[DataRow("0", true, false)]
		[DataRow("invalid", false, false)]
		[DataRow("invalid", true, true)]
		public void NiriFallback_EnvironmentOverridesOptInSetting(string? value, bool setting, bool enabled)
		{
			var env = NiriEnvironment();
			env.Remove("FILES_NIRI_SCALE");
			if (value is not null) env["FILES_NIRI_SCALE"] = value;
			var calls = 0;
			var result = DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, () => { calls++; return 1.25; }, setting);
			Assert.AreEqual(enabled ? "1.25" : null, result);
			Assert.AreEqual(enabled ? 1 : 0, calls);
		}

		[TestMethod]
		public void NiriFallback_ReadsSettingOnlyWhenNeeded()
		{
			var env = NiriEnvironment();
			var calls = 0;
			bool ReadSetting() { calls++; return true; }
			string? Resolve() => DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, () => 1.25,
				readCompositorScaleSetting: ReadSetting);

			Assert.AreEqual("1.25", Resolve());
			env["FILES_NIRI_SCALE"] = "0";
			Assert.IsNull(Resolve());
			env.Remove("FILES_NIRI_SCALE");
			env["GDK_SCALE"] = "1";
			Assert.IsNull(Resolve());
			env.Remove("GDK_SCALE");
			env.Remove("WAYLAND_DISPLAY");
			Assert.IsNull(Resolve());
			Assert.AreEqual(0, calls);
			env["WAYLAND_DISPLAY"] = "wayland-test";
			Assert.AreEqual("1.25", Resolve());
			Assert.AreEqual(1, calls);
			Assert.IsNull(DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, () => 1.25,
				readCompositorScaleSetting: () => throw new System.IO.IOException()));
		}


		[TestMethod]
		public void NiriFallback_DefaultIsOff()
		{
			var env = NiriEnvironment();
			env.Remove("FILES_NIRI_SCALE");
			var calls = 0;
			Assert.IsNull(DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, () => { calls++; return 2; }));
			Assert.AreEqual(0, calls);
		}

		[TestMethod]
		[DataRow("UNO_DISPLAY_SCALE_OVERRIDE", "1.5", null)]
		[DataRow("GDK_SCALE", "2", "2")]
		[DataRow("GDK_SCALE", "1", null)]
		[DataRow("QT_SCALE_FACTOR", "1.5", "1.5")]
		[DataRow("QT_SCREEN_SCALE_FACTORS", "DP-1=1.25", "1.25")]
		public void NiriFallback_DisplayHintsBeatForcedOnAndSetting(string variable, string value, string? expected)
		{
			var env = NiriEnvironment();
			env[variable] = value;
			var calls = 0;
			double? ReadScale() { calls++; return 2; }
			Assert.AreEqual(expected, DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, ReadScale, true));
			Assert.IsNull(DisplayScaleResolver.Resolve(NiriEnvironment().GetValueOrDefault, "Xft.dpi: 144", ReadScale, true));
			Assert.AreEqual(0, calls);
		}

		private static Dictionary<string, string> NiriEnvironment() => new()
		{
			["NIRI_SOCKET"] = "/tmp/files-test-niri.sock",
			["FILES_NIRI_SCALE"] = "1",
			["WAYLAND_DISPLAY"] = "wayland-test",
		};

		[TestMethod]
		public void NiriOutputs_OversizedFixture_ReturnsNull()
		{
			Assert.IsNull(DisplayScaleResolver.ParseNiriOutputs(new string(' ', 256 * 1024) + NiriOutputsFixture));
		}

		[TestMethod]
		public void NiriOutputs_MalformedFocusedScale_DoesNotUseAnotherOutput()
		{
			const string outputs = """{"DP-1":{"logical":{"scale":1.25}},"HDMI-A-1":{"logical":{}}}""";
			Assert.IsNull(DisplayScaleResolver.ParseNiriOutputs(outputs, NiriFocusedFixture));
		}

		[TestMethod]
		public void NiriFallback_OnlyQueriesWithoutOtherScaleHints()
		{
			var env = NiriEnvironment();
			var calls = 0;
			double? ReadScale() { calls++; return DisplayScaleResolver.ParseNiriOutputs(NiriOutputsFixture); }
			Assert.AreEqual("1.25", DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, ReadScale));
			Assert.AreEqual(1, calls);

			foreach (var variable in new[] { "UNO_DISPLAY_SCALE_OVERRIDE", "GDK_SCALE", "GDK_DPI_SCALE", "QT_SCALE_FACTOR", "QT_SCREEN_SCALE_FACTORS" })
			{
				foreach (var value in new[] { "1", "2", "garbage", " " })
				{
					env[variable] = value;
					DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, ReadScale);
					Assert.AreEqual(1, calls, variable + "=" + value);
				}
				env.Remove(variable);
			}

			foreach (var dpi in new[] { "96", "144", "120.0", "garbage", "" })
			{
				DisplayScaleResolver.Resolve(env.GetValueOrDefault, "Xft.dpi: " + dpi, ReadScale);
				Assert.AreEqual(1, calls, dpi);
			}
		}

		[TestMethod]
		public void NiriFallback_RequiresSessionAndHonoursOptOut()
		{
			foreach (var variable in new[] { "NIRI_SOCKET", "WAYLAND_DISPLAY", "FILES_NIRI_SCALE" })
			{
				var env = NiriEnvironment();
				if (variable == "FILES_NIRI_SCALE")
					env[variable] = "0";
				else
					env.Remove(variable);
				var calls = 0;
				Assert.IsNull(DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, () => { calls++; return 2; }));
				Assert.AreEqual(0, calls, variable);
			}
			var disabled = NiriEnvironment();
			disabled["FILES_NIRI_SCALE"] = "0";
			disabled["GDK_SCALE"] = "2";
			Assert.AreEqual("2", DisplayScaleResolver.Resolve(disabled.GetValueOrDefault, null));
		}

		[TestMethod]
		public void NiriFallback_ErrorsAndInvalidScales_LeaveUnoDetection()
		{
			var env = NiriEnvironment();
			Assert.IsNull(DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, () => throw new TimeoutException()));
			foreach (var scale in new double?[] { null, double.NaN, double.PositiveInfinity, -1, 0 })
				Assert.IsNull(DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, () => scale));
			Assert.AreEqual("1", DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, () => 0.5));
			Assert.AreEqual("4", DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, () => 8));
		}

	}
}
