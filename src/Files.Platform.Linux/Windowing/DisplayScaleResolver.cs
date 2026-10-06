// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Files.Platform.Linux.Windowing
{
	/// <summary>
	/// Works out the display scale to hand to Uno. Uno's X11 host reads <c>Xft.dpi</c> (set by KDE and GNOME on X11) and honours
	/// <c>UNO_DISPLAY_SCALE_OVERRIDE</c>; this fills the gap when <c>Xft.dpi</c> is absent (some XWayland sessions) from toolkit variables.
	/// </summary>
	public static class DisplayScaleResolver
	{
		public const string OverrideVariable = "UNO_DISPLAY_SCALE_OVERRIDE";

		/// <summary>
		/// Returns the value to assign to <see cref="OverrideVariable"/>, or null when Uno's own detection should be used.
		/// </summary>
		public static string? Resolve(Func<string, string?> getEnv, string? xResources)
		{
			if (!string.IsNullOrWhiteSpace(getEnv(OverrideVariable)))
				return null;

			if (HasXftDpi(xResources))
				return null;

			double? scale = ParseScale(getEnv("QT_SCALE_FACTOR"))
				?? ParseScreenFactors(getEnv("QT_SCREEN_SCALE_FACTORS"))
				?? ParseScale(getEnv("GDK_SCALE"));

			return scale is { } s && s > 1.0 ? s.ToString("0.##", CultureInfo.InvariantCulture) : null;
		}

		/// <summary>
		/// Returns the scale Uno will use (override variable, else Xft.dpi / 96), or 1.0 when unknown.
		/// </summary>
		public static double GetEffectiveScale(Func<string, string?> getEnv, string? xResources)
		{
			if (ParseScale(getEnv(OverrideVariable)) is { } overridden)
				return overridden;

			if (xResources is not null)
			{
				foreach (var line in xResources.Split('\n'))
				{
					var trimmed = line.TrimStart();
					if (!trimmed.StartsWith("Xft.dpi:", StringComparison.OrdinalIgnoreCase) && !trimmed.StartsWith("Xft*dpi:", StringComparison.OrdinalIgnoreCase))
						continue;

					if (double.TryParse(trimmed[(trimmed.IndexOf(':') + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dpi) && dpi >= 96)
						return Math.Min(dpi / 96.0, 4.0);
				}
			}

			return 1.0;
		}

		/// <summary>
		/// Returns the effective scale for the current process; call after <see cref="ApplyToProcess"/>.
		/// </summary>
		public static double GetEffectiveScale()
		{
			if (!OperatingSystem.IsLinux())
				return 1.0;

			try
			{
				return GetEffectiveScale(Environment.GetEnvironmentVariable, ReadXResources());
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				return 1.0;
			}
		}

		public static bool HasXftDpi(string? xResources)
		{
			if (string.IsNullOrEmpty(xResources))
				return false;

			foreach (var line in xResources.Split('\n'))
			{
				var trimmed = line.TrimStart();
				if (trimmed.StartsWith("Xft.dpi:", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("Xft*dpi:", StringComparison.OrdinalIgnoreCase))
					return true;
			}

			return false;
		}

		private static double? ParseScale(string? value)
		{
			if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
				return null;

			return scale is >= 1.0 and <= 4.0 ? scale : null;
		}

		private static double? ParseScreenFactors(string? value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var first = value.Split(';', StringSplitOptions.RemoveEmptyEntries)[0];
			var eq = first.IndexOf('=');
			return ParseScale(eq >= 0 ? first[(eq + 1)..] : first);
		}

		/// <summary>
		/// Applies <see cref="Resolve"/> to the process environment. Call before the Uno host starts.
		/// </summary>
		public static void ApplyToProcess()
		{
			if (!OperatingSystem.IsLinux())
				return;

			string? resolved;
			try
			{
				resolved = Resolve(Environment.GetEnvironmentVariable, ReadXResources());
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				return;
			}

			if (resolved is not null)
				Environment.SetEnvironmentVariable(OverrideVariable, resolved);
		}

		private static string? ReadXResources()
		{
			if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
				return null;

			var display = Native.X11Native.XOpenDisplay(0);
			if (display == 0)
				return null;

			try
			{
				return Marshal.PtrToStringUTF8(Native.X11Native.XResourceManagerString(display));
			}
			finally
			{
				Native.X11Native.XCloseDisplay(display);
			}
		}
	}
}
