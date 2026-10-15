// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Previews
{
	/// <summary>Isolates GTK, its locale/main loop, and untrusted document rendering from the app.</summary>
	public static class HtmlSnapshotRenderer
	{
		internal const int MaxPngBytes = 64 * 1024 * 1024;
		private static readonly Lazy<bool> available = new(CheckAvailable);
		public static bool IsAvailable => available.Value;

		private static bool CheckAvailable()
		{
			try
			{
				foreach (var library in new[] { "libwebkit2gtk-4.1.so.0", "libgtk-3.so.0" })
				{
					if (!NativeLibrary.TryLoad(library, out var handle)) return false;
					NativeLibrary.Free(handle);
				}
				return true;
			}
			catch (Exception) { return false; }
		}

		public static Task<byte[]?> RenderAsync(string path, int cssWidth, int scale, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!Path.IsPathFullyQualified(path) || !IsAvailable || Environment.ProcessPath is not { } processPath)
				return Task.FromResult<byte[]?>(null);
			var start = CreateStartInfo(processPath, Environment.GetCommandLineArgs()[0], path, cssWidth, scale);
			return RunProcessAsync(start, cancellationToken);
		}

		internal static ProcessStartInfo CreateStartInfo(string processPath, string entryPath, string path, int cssWidth, int scale)
		{
			var start = new ProcessStartInfo(processPath)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			// The CLR command line supplies the entry DLL without reflection (also safe for NativeAOT).
			if (Path.GetFileNameWithoutExtension(processPath) == "dotnet") start.ArgumentList.Add(Path.GetFullPath(entryPath));
			start.ArgumentList.Add(HtmlSnapshotHelper.Switch);
			start.ArgumentList.Add(path);
			start.ArgumentList.Add(Math.Clamp(cssWidth, 1, HtmlSnapshotHelper.MaxWidth).ToString(CultureInfo.InvariantCulture));
			start.ArgumentList.Add(HtmlSnapshotHelper.MaxHeight.ToString(CultureInfo.InvariantCulture));
			start.Environment[HtmlSnapshotHelper.ScaleVariable] = Math.Clamp(scale, 1, 2).ToString(CultureInfo.InvariantCulture);
			// The helper applies the scale itself; an inherited session GDK_SCALE would multiply it on X11.
			start.Environment["GDK_SCALE"] = "1";
			start.Environment["GDK_DPI_SCALE"] = "1";
			return start;
		}

		internal static async Task<byte[]?> RunProcessAsync(ProcessStartInfo start, CancellationToken cancellationToken, Action<int>? started = null)
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
			using var process = new Process { StartInfo = start };
			Task<byte[]>? output = null;
			Task? errors = null;
			try
			{
				linked.Token.ThrowIfCancellationRequested();
				if (!process.Start()) return null;
				// Kill while pipes are being read too, including when a child retains an inherited pipe.
				using var registration = linked.Token.Register(() => Kill(process));
				started?.Invoke(process.Id);
				output = ReadPngAsync(process.StandardOutput.BaseStream, MaxPngBytes, linked.Token);
				errors = DrainErrorsAsync(process.StandardError.BaseStream, linked.Token);
				// A cap/format failure must terminate the producer before waiting for its exit.
				var png = await output.ConfigureAwait(false);
				await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
				await errors.ConfigureAwait(false);
				cancellationToken.ThrowIfCancellationRequested();
				return process.ExitCode == 0 ? png : null;
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
			catch (Exception ex) when (ex is IOException or InvalidDataException or Win32Exception or InvalidOperationException)
			{
				cancellationToken.ThrowIfCancellationRequested();
				return null;
			}
			finally
			{
				Kill(process);
				linked.Cancel();
				try { if (process.Id > 0) await process.WaitForExitAsync().ConfigureAwait(false); }
				catch (InvalidOperationException) { }
				// Observe both readers on every exit, without retaining unbounded stderr in memory.
				try { if (output is not null) await output.ConfigureAwait(false); } catch (Exception) { }
				try { if (errors is not null) await errors.ConfigureAwait(false); } catch (Exception) { }
			}
		}

		private static void Kill(Process process)
		{
			try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
			catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
		}

		internal static bool HasPngSignature(ReadOnlySpan<byte> bytes) => bytes.StartsWith((ReadOnlySpan<byte>)[137, 80, 78, 71, 13, 10, 26, 10]);

		internal static async Task<byte[]> ReadPngAsync(Stream source, int maxBytes, CancellationToken token)
		{
			using var result = new MemoryStream();
			var buffer = new byte[8192];
			int count;
			while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
			{
				if (result.Length + count > maxBytes) throw new InvalidDataException("HTML snapshot exceeds output limit.");
				result.Write(buffer, 0, count);
				if (result.Length >= 8 && !HasPngSignature(result.GetBuffer().AsSpan(0, 8)))
					throw new InvalidDataException("Invalid HTML snapshot PNG signature.");
			}
			var bytes = result.ToArray();
			if (!HasPngSignature(bytes)) throw new InvalidDataException("Missing HTML snapshot PNG signature.");
			return bytes;
		}

		private static async Task DrainErrorsAsync(Stream source, CancellationToken token)
		{
			var buffer = new byte[4096];
			while (await source.ReadAsync(buffer, token).ConfigureAwait(false) != 0) { }
		}
	}
}
