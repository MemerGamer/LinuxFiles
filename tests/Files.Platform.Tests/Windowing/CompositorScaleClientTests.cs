// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Windowing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace Files.Platform.Tests.Windowing
{
	[TestClass]
	public sealed class CompositorScaleClientTests
	{
		[TestMethod]
		[DataRow("swaymsg", "[{\"name\":\"DP-1\",\"active\":true,\"scale\":1.25},{\"name\":\"DP-2\",\"active\":true,\"focused\":true,\"scale\":1.75}]", 1.75)]
		[DataRow("hyprctl", "[{\"name\":\"DP-1\",\"scale\":1.25,\"focused\":false},{\"name\":\"DP-2\",\"scale\":1.5,\"focused\":true}]", 1.5)]
		[DataRow("kscreen-doctor", "{\"outputs\":[{\"enabled\":false,\"scale\":2},{\"enabled\":true,\"connected\":true,\"scale\":1.25}]}", 1.25)]
		[DataRow("kscreen-doctor", "{\"outputs\":[{\"enabled\":true,\"priority\":2,\"scale\":2},{\"enabled\":true,\"priority\":1,\"scale\":1.5}]}", 1.5)]
		[DataRow("gsettings", "1.25\n", 1.25)]
		public void DesktopFixtures_PreserveFractionalScale(string provider, string fixture, double expected)
		{
			Assert.AreEqual(expected, CompositorScaleClient.ParseOutputs(provider, fixture));
		}

		[TestMethod]
		[DataRow("swaymsg", "[{\"active\":false,\"scale\":2}]")]
		[DataRow("hyprctl", "[{\"disabled\":true,\"scale\":2}]")]
		[DataRow("kscreen-doctor", "{\"outputs\":[{\"connected\":false,\"scale\":2}]}")]
		[DataRow("swaymsg", "[{\"scale\":2},{\"focused\":true,\"scale\":\"1.5\"}]")]
		[DataRow("hyprctl", "[{\"scale\":1e999}]")]
		[DataRow("gsettings", "NaN")]
		[DataRow("gsettings", "-1")]
		[DataRow("swaymsg", "{}")]
		[DataRow("kscreen-doctor", "garbage")]
		public void InvalidFixtures_ReturnNull(string provider, string fixture)
		{
			Assert.IsNull(CompositorScaleClient.ParseOutputs(provider, fixture));
		}

		[TestMethod]
		public void NonNiriSession_UsesSameOptInAndHonoursExplicitScale()
		{
			var env = new Dictionary<string, string> { ["WAYLAND_DISPLAY"] = "wayland-test" };
			Assert.IsNull(DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, () => 1.25));
			Assert.AreEqual("1.25", DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, () => 1.25, true));
			env["QT_SCALE_FACTOR"] = "1";
			Assert.IsNull(DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, () => throw new System.Exception(), true));
		}

		[TestMethod]
		public void GnomeX11Session_CanUseTextScale()
		{
			var env = new Dictionary<string, string> { ["XDG_CURRENT_DESKTOP"] = "ubuntu:GNOME" };
			Assert.AreEqual("1.25", DisplayScaleResolver.Resolve(env.GetValueOrDefault, null, () => 1.25, true));
		}
	}
}
