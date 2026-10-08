// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;

namespace Files.Platform.Linux.Windowing
{
	/// <summary>
	/// The rendering backend to request from Uno's X11 host.
	/// </summary>
	public enum RenderingBackendChoice
	{
		/// <summary>Uno's own order: Vulkan (opt-in), GLX, EGL, then software.</summary>
		Default,
		Software,
		OpenGL,
		OpenGLES,
		Vulkan,
	}

	/// <summary>
	/// Picks Uno's X11 rendering backend. Without a GPU, Mesa serves GL through llvmpipe, where Skia's GPU backend
	/// costs about twice the CPU of Uno's software renderer, so software rendering is used in that case.
	/// </summary>
	public static class RenderingBackendSelector
	{
		public const string OverrideVariable = "FILES_RENDERER";

		/// <summary>
		/// Returns the backend to use and why, from <see cref="OverrideVariable"/>, Mesa's software GL switches and GPU device presence.
		/// </summary>
		public static (RenderingBackendChoice Backend, string? Reason) Resolve(Func<string, string?> getEnv, Func<bool> hasGpuDevice)
		{
			var requested = getEnv(OverrideVariable)?.Trim();
			if (!string.IsNullOrEmpty(requested))
			{
				RenderingBackendChoice? choice = requested.ToLowerInvariant() switch
				{
					"software" => RenderingBackendChoice.Software,
					"opengl" or "gl" => RenderingBackendChoice.OpenGL,
					"gles" => RenderingBackendChoice.OpenGLES,
					"vulkan" => RenderingBackendChoice.Vulkan,
					// "auto" and unknown values fall through to detection
					_ => null,
				};

				if (choice is { } chosen)
					return (chosen, $"{OverrideVariable}={requested}");
			}

			if (IsTruthy(getEnv("LIBGL_ALWAYS_SOFTWARE")))
				return (RenderingBackendChoice.Software, "LIBGL_ALWAYS_SOFTWARE is set");

			var gallium = getEnv("GALLIUM_DRIVER")?.Trim();
			if (gallium is not null && (gallium.Equals("llvmpipe", StringComparison.OrdinalIgnoreCase) || gallium.Equals("softpipe", StringComparison.OrdinalIgnoreCase)))
				return (RenderingBackendChoice.Software, $"GALLIUM_DRIVER={gallium}");

			if (!hasGpuDevice())
				return (RenderingBackendChoice.Software, "no GPU device node (/dev/dri, /dev/nvidia*)");

			return (RenderingBackendChoice.Default, null);
		}

		/// <summary>
		/// Returns true when a DRM device (/dev/dri/card*, /dev/dri/renderD*) or an NVIDIA device node exists.
		/// Errors count as present so an unreadable /dev never forces software rendering.
		/// </summary>
		public static bool HasGpuDevice()
		{
			try
			{
				if (Directory.Exists("/dev/dri") && Directory.EnumerateFileSystemEntries("/dev/dri").Any(static p =>
				{
					var name = Path.GetFileName(p);
					return name.StartsWith("card", StringComparison.Ordinal) || name.StartsWith("renderD", StringComparison.Ordinal);
				}))
					return true;

				return Directory.EnumerateFileSystemEntries("/dev", "nvidia*").Any();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return true;
			}
		}

		private static bool IsTruthy(string? value)
		{
			var trimmed = value?.Trim();
			return !string.IsNullOrEmpty(trimmed) && trimmed != "0" && !trimmed.Equals("false", StringComparison.OrdinalIgnoreCase);
		}
	}
}
