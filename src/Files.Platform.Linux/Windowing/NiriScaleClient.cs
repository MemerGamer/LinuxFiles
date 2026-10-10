// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Launching;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Windowing
{
	internal static class NiriScaleClient
	{
		internal const int MaxResponseLength = 256 * 1024;
		private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

		internal static double? ReadScale(out string? source)
		{
			source = null;
			var socketPath = Environment.GetEnvironmentVariable("NIRI_SOCKET");
			using (var timeout = new CancellationTokenSource(Timeout))
			{
				var outputs = TrySocketRequest(socketPath, "Outputs", timeout.Token);
				if (outputs is not null)
				{
					var focused = TrySocketRequest(socketPath, "FocusedOutput", timeout.Token);
					if (DisplayScaleResolver.ParseNiriOutputs(outputs, focused) is { } scale)
					{
						source = "IPC";
						return scale;
					}
				}
			}

			try
			{
				var binary = new PathExecutableLocator().Locate("niri");
				if (binary is null)
					return null;
				using var timeout = new CancellationTokenSource(Timeout);
				var outputs = TryCommand(binary, "outputs", timeout.Token);
				if (outputs is null)
					return null;
				var focused = TryCommand(binary, "focused-output", timeout.Token);
				source = "CLI";
				return DisplayScaleResolver.ParseNiriOutputs(outputs, focused);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static string? TrySocketRequest(string? path, string request, CancellationToken cancellationToken)
		{
			try
			{
				return SocketRequestAsync(path, request, cancellationToken).GetAwaiter().GetResult();
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static async Task<string?> SocketRequestAsync(string? path, string request, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(path))
				return null;
			using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellationToken).ConfigureAwait(false);
			using var stream = new NetworkStream(socket);
			// Unit variants in niri's serde protocol are JSON strings, followed by a newline.
			await stream.WriteAsync(Encoding.UTF8.GetBytes($"\"{request}\"\n"), cancellationToken).ConfigureAwait(false);
			using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
			return await ReadBoundedAsync(reader, true, cancellationToken).ConfigureAwait(false);
		}

		private static string? TryCommand(string binary, string command, CancellationToken cancellationToken)
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				var info = new ProcessStartInfo(binary)
				{
					UseShellExecute = false,
					RedirectStandardInput = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true,
				};
				foreach (var argument in new[] { "msg", "--json", command })
					info.ArgumentList.Add(argument);
				using var process = TracedProcess.Start(info);
				if (process is null)
					return null;
				try
				{
					process.StandardInput.Close();
					var output = ReadBoundedAsync(process.StandardOutput, false, cancellationToken);
					var error = ReadBoundedAsync(process.StandardError, false, cancellationToken);
					Task.WhenAll(process.WaitForExitAsync(cancellationToken), output, error).GetAwaiter().GetResult();
					return process.ExitCode == 0 ? output.Result : null;
				}
				finally
				{
					if (!process.HasExited)
					{
						process.Kill(entireProcessTree: true);
						process.WaitForExit((int)Timeout.TotalMilliseconds);
					}
				}
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static async Task<string?> ReadBoundedAsync(StreamReader reader, bool singleLine, CancellationToken cancellationToken)
		{
			var buffer = new char[4096];
			var result = new StringBuilder();
			int count;
			while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
			{
				var newline = singleLine ? Array.IndexOf(buffer, '\n', 0, count) : -1;
				var length = newline >= 0 ? newline : count;
				if (result.Length + length > MaxResponseLength)
					throw new IOException("niri response exceeds the size limit.");
				result.Append(buffer, 0, length);
				if (newline >= 0)
					return result.ToString();
			}
			return result.ToString();
		}
	}
}
