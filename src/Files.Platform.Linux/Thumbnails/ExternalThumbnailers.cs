// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Thumbnails
{
	/// <summary>
	/// A parsed <c>/usr/share/thumbnailers/*.thumbnailer</c> entry.
	/// </summary>
	public sealed record ThumbnailerEntry(string Exec, string? TryExec, IReadOnlyList<string> MimeTypes)
	{
		/// <summary>
		/// Parses the contents of a <c>.thumbnailer</c> file, or returns <see langword="null"/> when it is unusable.
		/// </summary>
		public static ThumbnailerEntry? Parse(string content)
		{
			string? exec = null, tryExec = null, mimes = null;
			var inEntry = false;
			foreach (var raw in content.Split('\n'))
			{
				var line = raw.Trim();
				if (line.StartsWith('['))
				{
					inEntry = line == "[Thumbnailer Entry]";
					continue;
				}

				if (!inEntry || line.Length == 0 || line[0] == '#')
					continue;

				var eq = line.IndexOf('=');
				if (eq <= 0)
					continue;

				var key = line[..eq].Trim();
				var value = line[(eq + 1)..].Trim();
				switch (key)
				{
					case "Exec": exec = value; break;
					case "TryExec": tryExec = value; break;
					case "MimeType": mimes = value; break;
				}
			}

			if (string.IsNullOrEmpty(exec) || string.IsNullOrEmpty(mimes))
				return null;

			var list = mimes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			return list.Length == 0 ? null : new ThumbnailerEntry(exec, tryExec, list);
		}

		/// <summary>
		/// Expands the Exec line into a program and arguments using the spec's <c>%s %i %u %o %%</c> codes.
		/// </summary>
		public (string FileName, IReadOnlyList<string> Arguments)? BuildCommand(string inputPath, string inputUri, string outputPath, uint size)
		{
			var tokens = Tokenize(Exec);
			if (tokens.Count == 0 || tokens[0].Length == 0 || (tokens[0].Contains('/') && !tokens[0].StartsWith('/')))
				return null;

			// Always hand the thumbnailer an absolute path so a name starting with '-' cannot be read as an option.
			inputPath = Path.GetFullPath(inputPath);
			if (!inputPath.StartsWith('/') || !inputUri.StartsWith("file:///", StringComparison.Ordinal))
				return null;

			var args = new List<string>(tokens.Count);
			for (var i = 1; i < tokens.Count; i++)
				args.Add(Expand(tokens[i], inputPath, inputUri, outputPath, size));

			return (tokens[0], args);
		}

		private static string Expand(string token, string input, string uri, string output, uint size)
		{
			var sb = new StringBuilder(token.Length + 16);
			for (var i = 0; i < token.Length; i++)
			{
				if (token[i] != '%' || i + 1 >= token.Length)
				{
					sb.Append(token[i]);
					continue;
				}

				switch (token[++i])
				{
					case 's': sb.Append(size); break;
					case 'i': sb.Append(input); break;
					case 'u': sb.Append(uri); break;
					case 'o': sb.Append(output); break;
					case '%': sb.Append('%'); break;
					default: sb.Append('%').Append(token[i]); break;
				}
			}

			return sb.ToString();
		}

		private static List<string> Tokenize(string exec)
		{
			var tokens = new List<string>();
			var sb = new StringBuilder();
			char quote = '\0';
			var has = false;
			foreach (var c in exec)
			{
				if (quote != '\0')
				{
					if (c == quote)
						quote = '\0';
					else
						sb.Append(c);
				}
				else if (c is '"' or '\'')
				{
					quote = c;
					has = true;
				}
				else if (char.IsWhiteSpace(c))
				{
					if (has || sb.Length > 0)
						tokens.Add(sb.ToString());
					sb.Clear();
					has = false;
				}
				else
				{
					sb.Append(c);
				}
			}

			if (has || sb.Length > 0)
				tokens.Add(sb.ToString());

			return tokens;
		}
	}

	/// <summary>
	/// Maps MIME types to the thumbnailer entries installed on the system.
	/// </summary>
	public sealed class ThumbnailerRegistry
	{
		private readonly Dictionary<string, ThumbnailerEntry> _byMime = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Loads every <c>*.thumbnailer</c> file under the given directories; earlier directories take priority.
		/// </summary>
		public static ThumbnailerRegistry Load(IEnumerable<string> directories)
		{
			var registry = new ThumbnailerRegistry();
			foreach (var dir in directories)
			{
				if (!Directory.Exists(dir))
					continue;

				string[] files;
				try
				{
					files = Directory.GetFiles(dir, "*.thumbnailer");
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					continue;
				}

				Array.Sort(files, StringComparer.Ordinal);
				foreach (var file in files)
				{
					ThumbnailerEntry? entry;
					try
					{
						entry = ThumbnailerEntry.Parse(File.ReadAllText(file));
					}
					catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
					{
						continue;
					}

					if (entry is null)
						continue;

					foreach (var mime in entry.MimeTypes)
						registry._byMime.TryAdd(mime, entry);
				}
			}

			return registry;
		}

		/// <summary>
		/// Finds the thumbnailer registered for a MIME type.
		/// </summary>
		public ThumbnailerEntry? Find(string mimeType) => _byMime.GetValueOrDefault(mimeType);
	}

	/// <summary>
	/// Builds <c>bwrap</c> (bubblewrap) command lines that run a thumbnailer read-only and without network.
	/// </summary>
	public static class BubblewrapSandbox
	{
		/// <summary>
		/// Gets whether <c>bwrap</c> is on PATH.
		/// </summary>
		public static bool IsAvailable()
		{
			foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries))
			{
				if (File.Exists(Path.Combine(dir, "bwrap")))
					return true;
			}

			return false;
		}

		/// <summary>
		/// Wraps a command so it runs with a read-only root and write access only to <paramref name="writableDirectory"/>.
		/// </summary>
		public static (string FileName, IReadOnlyList<string> Arguments) Wrap(string fileName, IReadOnlyList<string> arguments, string writableDirectory)
		{
			var args = new List<string>
			{
				"--ro-bind", "/", "/",
				"--dev", "/dev",
				"--proc", "/proc",
				"--tmpfs", "/tmp",
				"--bind", writableDirectory, writableDirectory,
				"--unshare-all",
				"--die-with-parent",
				"--",
				fileName,
			};
			args.AddRange(arguments);
			return ("bwrap", args);
		}
	}

	/// <summary>
	/// Seam for launching thumbnailer processes.
	/// </summary>
	public interface IThumbnailerProcessRunner
	{
		/// <summary>
		/// Runs a program to completion and returns whether it exited with code zero.
		/// </summary>
		Task<bool> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
	}

	/// <summary>
	/// Launches thumbnailers as real child processes with a timeout.
	/// </summary>
	public sealed class ThumbnailerProcessRunner : IThumbnailerProcessRunner
	{
		private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

		/// <inheritdoc/>
		public async Task<bool> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
		{
			var info = new ProcessStartInfo(fileName)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			foreach (var arg in arguments)
				info.ArgumentList.Add(arg);

			Process? process;
			try
			{
				process = Process.Start(info);
			}
			catch (System.ComponentModel.Win32Exception)
			{
				return false;
			}

			if (process is null)
				return false;

			using (process)
			{
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();
				using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				cts.CancelAfter(Timeout);
				try
				{
					await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
					return process.ExitCode == 0;
				}
				catch (OperationCanceledException)
				{
					try { process.Kill(true); } catch (InvalidOperationException) { }
					cancellationToken.ThrowIfCancellationRequested();
					return false;
				}
			}
		}
	}
}
