// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Windowing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace Files.Platform.Tests.Windowing
{
	[TestClass]
	public sealed class DisplayScaleResolverTests
	{
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
	}
}
