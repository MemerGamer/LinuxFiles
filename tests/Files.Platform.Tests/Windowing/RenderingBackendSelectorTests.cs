// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Windowing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

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
