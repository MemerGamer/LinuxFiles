// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Windowing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;

namespace Files.Platform.Tests.Windowing
{
	[TestClass]
	public sealed class RenderingBackendSelectorTests
	{
		private static RenderingBackendChoice Resolve(bool hasGpu, params (string Key, string Value)[] env)
		{
			var map = new Dictionary<string, string>();
			foreach (var (key, value) in env)
				map[key] = value;

			return RenderingBackendSelector.Resolve(k => map.GetValueOrDefault(k), () => hasGpu).Backend;
		}

		[TestMethod]
		public void Gpu_KeepsUnoDefault()
		{
			Assert.AreEqual(RenderingBackendChoice.Default, Resolve(true));
			Assert.IsNull(RenderingBackendSelector.Resolve(_ => null, () => true).Reason);
		}

		[TestMethod]
		public void NoGpu_UsesSoftware()
		{
			Assert.AreEqual(RenderingBackendChoice.Software, Resolve(false));
		}

		[TestMethod]
		public void MesaSoftwareSwitches_UseSoftware()
		{
			Assert.AreEqual(RenderingBackendChoice.Software, Resolve(true, ("LIBGL_ALWAYS_SOFTWARE", "1")));
			Assert.AreEqual(RenderingBackendChoice.Software, Resolve(true, ("GALLIUM_DRIVER", "llvmpipe")));
			Assert.AreEqual(RenderingBackendChoice.Default, Resolve(true, ("LIBGL_ALWAYS_SOFTWARE", "0")));
			Assert.AreEqual(RenderingBackendChoice.Default, Resolve(true, ("GALLIUM_DRIVER", "radeonsi")));
		}

		[TestMethod]
		public void LibGlAlwaysSoftware_FollowsMesaBooleanParsing()
		{
			foreach (var value in new[] { "", " ", "0", "n", "no", "NO", "f", "F", "false", "False", "off", "OFF" })
				Assert.AreEqual(RenderingBackendChoice.Default, Resolve(true, ("LIBGL_ALWAYS_SOFTWARE", value)), $"'{value}'");

			foreach (var value in new[] { "1", "y", "yes", "true", "on", "TRUE", "2", "anything" })
				Assert.AreEqual(RenderingBackendChoice.Software, Resolve(true, ("LIBGL_ALWAYS_SOFTWARE", value)), $"'{value}'");
		}

		[TestMethod]
		public void DeviceProbe_FindsNodesAndTreatsErrorsAsGpu()
		{
			var root = Directory.CreateTempSubdirectory("files-gpu-probe-").FullName;
			try
			{
				Assert.IsFalse(RenderingBackendSelector.HasGpuDevice(root));

				Directory.CreateDirectory(Path.Combine(root, "dri"));
				File.WriteAllText(Path.Combine(root, "dri", "by-path"), string.Empty);
				Assert.IsFalse(RenderingBackendSelector.HasGpuDevice(root));

				File.WriteAllText(Path.Combine(root, "dri", "renderD128"), string.Empty);
				Assert.IsTrue(RenderingBackendSelector.HasGpuDevice(root));

				File.Delete(Path.Combine(root, "dri", "renderD128"));
				File.WriteAllText(Path.Combine(root, "nvidia0"), string.Empty);
				Assert.IsTrue(RenderingBackendSelector.HasGpuDevice(root));

				// A probe that cannot enumerate the directory must not force software rendering
				var notADirectory = Path.Combine(root, "nvidia0");
				Assert.IsTrue(RenderingBackendSelector.HasGpuDevice(notADirectory));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[TestMethod]
		public void Override_WinsOverDetection()
		{
			Assert.AreEqual(RenderingBackendChoice.OpenGL, Resolve(false, ("FILES_RENDERER", "opengl"), ("LIBGL_ALWAYS_SOFTWARE", "1")));
			Assert.AreEqual(RenderingBackendChoice.OpenGLES, Resolve(true, ("FILES_RENDERER", " GLES ")));
			Assert.AreEqual(RenderingBackendChoice.Vulkan, Resolve(true, ("FILES_RENDERER", "vulkan")));
			Assert.AreEqual(RenderingBackendChoice.Software, Resolve(true, ("FILES_RENDERER", "software")));
		}

		[TestMethod]
		public void AutoOrUnknownOverride_FallsBackToDetection()
		{
			Assert.AreEqual(RenderingBackendChoice.Default, Resolve(true, ("FILES_RENDERER", "auto")));
			Assert.AreEqual(RenderingBackendChoice.Software, Resolve(false, ("FILES_RENDERER", "metal")));
		}
	}
}
