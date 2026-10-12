// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Launching;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Windowing
{
	/// <summary>Queries only the active desktop, with one deadline shared by all requests.</summary>
	public static class CompositorScaleClient
	{
		internal static bool HasSession(Func<string, string?> getEnv) =>
			!string.IsNullOrWhiteSpace(getEnv("WAYLAND_DISPLAY")) ||
			Desktop(getEnv, "KDE") || Desktop(getEnv, "GNOME");

		private static bool Desktop(Func<string, string?> getEnv, string name) =>
			(getEnv("XDG_CURRENT_DESKTOP") ?? "").Split(':').Contains(name, StringComparer.OrdinalIgnoreCase);

		public static double? ReadScale(out string? source, CancellationToken cancellationToken = default)
		{
			source = null;
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(TimeSpan.FromMilliseconds(500));
			Func<string, string?> env = Environment.GetEnvironmentVariable;
			if (!string.IsNullOrWhiteSpace(env("NIRI_SOCKET")) || Desktop(env, "niri"))
			{
				var scale = NiriScaleClient.ReadScale(out var transport, deadline.Token);
				source = scale is null ? null : $"niri {transport}";
				return scale;
			}
			string? binary = null;
			string[] arguments = [];
			if (!string.IsNullOrWhiteSpace(env("SWAYSOCK")) || Desktop(env, "sway"))
			{
				binary = "swaymsg";
				arguments = ["-r", "-t", "get_outputs"];
			}
			else if (!string.IsNullOrWhiteSpace(env("HYPRLAND_INSTANCE_SIGNATURE")) || Desktop(env, "Hyprland"))
			{
				binary = "hyprctl";
				arguments = ["-j", "monitors"];
			}
			else if (Desktop(env, "KDE"))
			{
				binary = "kscreen-doctor";
				arguments = ["--json"];
			}
			else if (Desktop(env, "GNOME"))
			{
				binary = "gsettings";
				arguments = ["get", "org.gnome.desktop.interface", "text-scaling-factor"];
			}
			if (binary is null) return null;
			try
			{
				var path = new PathExecutableLocator().Locate(binary);
				if (path is null) return null;
				var scale = ParseOutputs(binary, NiriScaleClient.TryCommand(path, arguments, deadline.Token));
				source = scale is null ? null : binary;
				return scale;
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>Re-detects output/focus changes without running IPC on the UI thread.</summary>
		public static async Task WatchAsync(Action<double> changed, CancellationToken cancellationToken, Func<bool>? enabled = null)
		{
			double? previous = null;
			using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
			try
			{
				while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
				{
					if (enabled is not null && !enabled()) continue;
					var scale = ReadScale(out _, cancellationToken);
					if (scale is { } current && current != previous && !cancellationToken.IsCancellationRequested)
					{
						previous = current;
						changed(current);
					}
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		}

		/// <summary>Prefers the focused output; disabled outputs never supply a fallback.</summary>
		public static double? ParseOutputs(string provider, string? json)
		{
			if (string.IsNullOrWhiteSpace(json) || json.Length > NiriScaleClient.MaxResponseLength) return null;
			if (provider == "gsettings")
				return double.TryParse(json.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var textScale)
					? ValidScale(textScale) : null;
			try
			{
				using var document = JsonDocument.Parse(json);
				var outputs = document.RootElement;
				if (provider == "kscreen-doctor" && outputs.ValueKind == JsonValueKind.Object && outputs.TryGetProperty("outputs", out var kdeOutputs))
					outputs = kdeOutputs;
				if (outputs.ValueKind != JsonValueKind.Array || provider is not ("swaymsg" or "hyprctl" or "kscreen-doctor")) return null;
				double? first = null;
				double? primary = null;
				foreach (var output in outputs.EnumerateArray())
				{
					if (output.ValueKind != JsonValueKind.Object || IsFalse(output, "active") || IsFalse(output, "enabled") ||
						IsFalse(output, "connected") || IsTrue(output, "disabled")) continue;
					var scale = output.TryGetProperty("scale", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
						? ValidScale(number) : null;
					if (IsTrue(output, "focused")) return scale;
					first ??= scale;
					if (provider == "kscreen-doctor" && output.TryGetProperty("priority", out var priority) &&
						priority.ValueKind == JsonValueKind.Number && priority.TryGetInt32(out var rank) && rank == 1)
						primary = scale;
				}
				return primary ?? first;
			}
			catch (JsonException)
			{
				return null;
			}
		}

		private static bool IsTrue(JsonElement value, string property) => value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.True;
		private static bool IsFalse(JsonElement value, string property) => value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.False;
		private static double? ValidScale(double scale) => double.IsFinite(scale) && scale > 0 ? Math.Clamp(scale, 1, 4) : null;
	}
}
