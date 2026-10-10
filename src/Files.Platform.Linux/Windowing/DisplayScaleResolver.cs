// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace Files.Platform.Linux.Windowing
{
	/// <summary>
	/// Works out the display scale to hand to Uno. Uno's X11 host reads <c>Xft.dpi</c> (set by KDE and GNOME on X11) and honours
	/// <c>UNO_DISPLAY_SCALE_OVERRIDE</c>; this fills the gap when <c>Xft.dpi</c> is absent (some XWayland sessions) from toolkit variables or niri IPC.
	/// </summary>
	public static class DisplayScaleResolver
	{
		public const string OverrideVariable = "UNO_DISPLAY_SCALE_OVERRIDE";

		private static int niriSourceLogged;

		/// <summary>
		/// Returns the value to assign to <see cref="OverrideVariable"/>, or null when Uno's own detection should be used.
		/// </summary>
		public static string? Resolve(Func<string, string?> getEnv, string? xResources, Func<double?>? readNiriScale = null,
			bool detectCompositorDisplayScale = false, Func<bool>? readCompositorScaleSetting = null)
		{
			if (!string.IsNullOrWhiteSpace(getEnv(OverrideVariable)))
				return null;

			if (ParseXftDpi(xResources) is { } dpi)
			{
				// Uno only accepts an integer Xft.dpi and uses scale 1.0 for values such as "120.5" or "144.0"
				if (int.TryParse(GetXftDpiText(xResources), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
					return null;

				return dpi > 96 ? Math.Min(dpi / 96.0, 4.0).ToString("0.###", CultureInfo.InvariantCulture) : null;
			}

			// Qt multiplies the global factor by the per-screen factor
			var global = ParseScale(getEnv("QT_SCALE_FACTOR"));
			var screen = ParseScreenFactors(getEnv("QT_SCREEN_SCALE_FACTORS"));
			double? scale = global is null && screen is null ? null : (global ?? 1.0) * (screen ?? 1.0);
			scale ??= ParseScale(getEnv("GDK_SCALE"));

			if (scale is { } s && s > 1.0)
				return Math.Min(s, 4.0).ToString("0.##", CultureInfo.InvariantCulture);

			if (string.IsNullOrWhiteSpace(getEnv("NIRI_SOCKET")) || string.IsNullOrWhiteSpace(getEnv("WAYLAND_DISPLAY")) ||
				GetXftDpiText(xResources) is not null)
				return null;

			foreach (var variable in new[] { OverrideVariable, "GDK_SCALE", "GDK_DPI_SCALE", "QT_SCALE_FACTOR", "QT_SCREEN_SCALE_FACTORS" })
				if (!string.IsNullOrEmpty(getEnv(variable)))
					return null;

			try
			{
				if (!(getEnv("FILES_NIRI_SCALE") switch
				{
					"1" => true,
					"0" => false,
					_ => readCompositorScaleSetting?.Invoke() ?? detectCompositorDisplayScale,
				}))
					return null;

				return readNiriScale?.Invoke() is { } niri && double.IsFinite(niri) && niri > 0
					? Math.Clamp(niri, 1.0, 4.0).ToString("0.###", CultureInfo.InvariantCulture) : null;
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>
		/// Reads niri's socket reply or CLI output, preferring the separately queried focused output.
		/// </summary>
		public static double? ParseNiriOutputs(string? outputsJson, string? focusedOutputJson = null)
		{
			if (string.IsNullOrWhiteSpace(outputsJson) || outputsJson.Length > NiriScaleClient.MaxResponseLength)
				return null;

			try
			{
				using var document = JsonDocument.Parse(outputsJson);
				var outputs = UnwrapNiriReply(document.RootElement, "Outputs");
				if (outputs.ValueKind != JsonValueKind.Object)
					return null;

				var focusedName = ParseNiriFocusedName(focusedOutputJson);
				JsonElement? first = null;
				foreach (var output in outputs.EnumerateObject())
				{
					first ??= output.Value;
					if (output.Name == focusedName)
						return ParseNiriOutputScale(output.Value);
				}
				return first is { } fallback ? ParseNiriOutputScale(fallback) : null;
			}
			catch (JsonException)
			{
				return null;
			}
		}

		private static JsonElement UnwrapNiriReply(JsonElement root, string response)
		{
			if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Err", out _))
				return default;
			if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Ok", out var ok))
				return ok.ValueKind == JsonValueKind.Object && ok.TryGetProperty(response, out var result) ? result : default;
			return root;
		}

		private static string? ParseNiriFocusedName(string? json)
		{
			if (string.IsNullOrWhiteSpace(json) || json.Length > NiriScaleClient.MaxResponseLength)
				return null;
			try
			{
				using var document = JsonDocument.Parse(json);
				var output = UnwrapNiriReply(document.RootElement, "FocusedOutput");
				return output.ValueKind == JsonValueKind.Object && output.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
					? name.GetString() : null;
			}
			catch (JsonException)
			{
				return null;
			}
		}

		private static double? ParseNiriOutputScale(JsonElement output)
		{
			if (output.ValueKind != JsonValueKind.Object || !output.TryGetProperty("logical", out var logical) ||
				logical.ValueKind != JsonValueKind.Object || !logical.TryGetProperty("scale", out var value) ||
				value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var scale) || !double.IsFinite(scale) || scale <= 0)
				return null;
			return Math.Clamp(scale, 1.0, 4.0);
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
			return double.TryParse(GetXftDpiText(xResources), NumberStyles.Float, CultureInfo.InvariantCulture, out var dpi) && double.IsFinite(dpi) && dpi > 0 ? dpi : null;
		}

		private static string? GetXftDpiText(string? xResources)
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
				if (name.Equals("Xft.dpi", StringComparison.OrdinalIgnoreCase) || name.Equals("Xft*dpi", StringComparison.OrdinalIgnoreCase))
					return trimmed[(colon + 1)..].Trim();
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
			string? niriSource = null;
			try
			{
				resolved = Resolve(Environment.GetEnvironmentVariable, ReadXResources(),
					() => NiriScaleClient.ReadScale(out niriSource),
					readCompositorScaleSetting: () => CompositorScaleSettings.Read(new LinuxAppDataPaths().UserSettingsFilePath));
			}
			catch (Exception)
			{
				// Scale detection must never block startup
				return;
			}

			if (resolved is not null)
			{
				Environment.SetEnvironmentVariable(OverrideVariable, resolved);
				if (niriSource is not null && Interlocked.Exchange(ref niriSourceLogged, 1) == 0)
				{
					try { Console.Error.WriteLine($"[display-scale] Using niri {niriSource}: {resolved}"); }
					catch (System.IO.IOException) { }
				}
			}
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
