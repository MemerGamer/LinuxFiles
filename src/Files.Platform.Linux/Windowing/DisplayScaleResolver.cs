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

			if (ParseXftDpi(xResources) is not null)
				return null;

			// Qt multiplies the global factor by the per-screen factor
			var global = ParseScale(getEnv("QT_SCALE_FACTOR"));
			var screen = ParseScreenFactors(getEnv("QT_SCREEN_SCALE_FACTORS"));
			double? scale = global is null && screen is null ? null : (global ?? 1.0) * (screen ?? 1.0);
			scale ??= ParseScale(getEnv("GDK_SCALE"));

			return scale is { } s && s > 1.0 ? Math.Min(s, 4.0).ToString("0.##", CultureInfo.InvariantCulture) : null;
		}

		/// <summary>
		/// Returns the scale Uno will use (override variable, else Xft.dpi / 96), or 1.0 when unknown.
		/// </summary>
		public static double GetEffectiveScale(Func<string, string?> getEnv, string? xResources)
		{
			if (ParseScale(getEnv(OverrideVariable)) is { } overridden)
				return overridden;

			return ParseXftDpi(xResources) is { } dpi && dpi >= 96 ? Math.Min(dpi / 96.0, 4.0) : 1.0;
		}

		/// <summary>
		/// Returns the Xft.dpi value from X resources (tolerating whitespace around the colon), or null.
		/// </summary>
		public static double? ParseXftDpi(string? xResources)
		{
			if (string.IsNullOrEmpty(xResources))
				return null;

			foreach (var line in xResources.Split('\n'))
			{
				var trimmed = line.Trim();
				var colon = trimmed.IndexOf(':');
				if (colon < 0)
					continue;

				var name = trimmed[..colon].TrimEnd();
				if (!name.Equals("Xft.dpi", StringComparison.OrdinalIgnoreCase) && !name.Equals("Xft*dpi", StringComparison.OrdinalIgnoreCase))
					continue;

				if (double.TryParse(trimmed[(colon + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dpi) && dpi > 0)
					return dpi;
			}

			return null;
		}

		public static bool HasXftDpi(string? xResources) => ParseXftDpi(xResources) is not null;

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

			foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				var eq = entry.IndexOf('=');
				if (ParseScale(eq >= 0 ? entry[(eq + 1)..] : entry) is { } scale)
					return scale;
			}

			return null;
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
			catch (Exception)
			{
				return 1.0;
			}
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
			catch (Exception)
			{
				// Scale detection must never block startup
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
