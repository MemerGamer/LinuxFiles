// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Enumeration;
using Files.Platform.Abstractions.Search;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Search
{
	/// <summary>
	/// Recursive search on top of <see cref="IFileSystemEnumerator"/>. The walk is done folder by folder so depth, time and result caps
	/// can prune it, and symlinked folders are never entered (no cycles).
	/// </summary>
	public sealed class LinuxFileSearchService : IFileSearchService
	{
		private readonly IFileSystemEnumerator _enumerator;

		public LinuxFileSearchService(IFileSystemEnumerator enumerator)
		{
			_enumerator = enumerator;
		}

		/// <summary>
		/// Builds the name predicate: a case-insensitive substring, or an anchored case-insensitive glob when the pattern contains <c>*</c> or <c>?</c>.
		/// </summary>
		public static Func<string, bool> CreateNameMatcher(string pattern)
		{
			if (pattern.AsSpan().IndexOfAny('*', '?') < 0)
				return name => name.Contains(pattern, StringComparison.OrdinalIgnoreCase);

			var sb = new StringBuilder("^");
			foreach (var c in pattern)
			{
				sb.Append(c switch
				{
					'*' => ".*",
					'?' => ".",
					_ => Regex.Escape(c.ToString()),
				});
			}
			sb.Append('$');

			var regex = new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(250));
			return name =>
			{
				try { return regex.IsMatch(name); }
				catch (RegexMatchTimeoutException) { return false; }
			};
		}

		/// <inheritdoc/>
		public async IAsyncEnumerable<FileSearchMatch> SearchAsync(string rootPath, string pattern, FileSearchOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			options ??= new FileSearchOptions();
			if (string.IsNullOrEmpty(pattern))
				yield break;

			if (!Directory.Exists(rootPath))
				throw new DirectoryNotFoundException(rootPath);

			using var timeoutCts = new CancellationTokenSource();
			if (options.Timeout > TimeSpan.Zero)
				timeoutCts.CancelAfter(options.Timeout);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
			var token = linked.Token;

			var matcher = options.SearchContent ? null : CreateNameMatcher(pattern);
			var enumOptions = new FileSystemEnumerationOptions { IncludeHidden = options.IncludeHidden, FollowSymlinks = false, Recursive = false };
			var pending = new Queue<(string Path, int Depth)>();
			pending.Enqueue((rootPath, 0));
			var count = 0;
			var timedOut = false;

			while (pending.Count > 0 && !timedOut)
			{
				var (folder, depth) = pending.Dequeue();
				var enumerator = _enumerator.EnumerateAsync(folder, enumOptions, token).GetAsyncEnumerator(token);
				try
				{
					while (true)
					{
						FileSystemEntryInfo entry;
						try
						{
							if (!await enumerator.MoveNextAsync())
								break;
							entry = enumerator.Current;
						}
						catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
						{
							timedOut = true; // time cap
							break;
						}
						catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
						{
							break; // folder vanished or unreadable
						}

						if (entry.IsHidden && !options.IncludeHidden)
							continue;

						if (entry.IsDirectory && !entry.IsSymlink && depth < options.MaxDepth)
							pending.Enqueue((entry.FullPath, depth + 1));

						string? snippet = null;
						bool matched;
						if (matcher is not null)
						{
							matched = matcher(entry.Name);
						}
						else if (!entry.IsDirectory && entry.Length <= options.MaxContentFileSize)
						{
							var path = entry.FullPath;
							try
							{
								snippet = await Task.Run(() => FindInContent(path, pattern, token), CancellationToken.None);
							}
							catch (OperationCanceledException)
							{
							}
							matched = snippet is not null;
						}
						else
						{
							matched = false;
						}

						if (token.IsCancellationRequested)
						{
							cancellationToken.ThrowIfCancellationRequested();
							yield break;
						}

						if (!matched)
							continue;

						yield return new FileSearchMatch(entry, snippet);

						if (options.MaxResults > 0 && ++count >= options.MaxResults)
							yield break;
					}
				}
				finally
				{
					await enumerator.DisposeAsync();
				}
			}
		}

		/// <summary>
		/// Returns the first line containing <paramref name="needle"/> (case-insensitive) of a text file, or <see langword="null"/>
		/// when there is none or the file looks binary (a NUL in the first 4 KiB).
		/// </summary>
		internal static string? FindInContent(string path, string needle, CancellationToken token)
		{
			try
			{
				using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
				var head = new byte[4096];
				var read = stream.Read(head, 0, head.Length);
				if (Array.IndexOf(head, (byte)0, 0, read) >= 0)
					return null;

				stream.Position = 0;
				using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
				string? line;
				while ((line = reader.ReadLine()) is not null)
				{
					token.ThrowIfCancellationRequested();
					if (line.Contains(needle, StringComparison.OrdinalIgnoreCase))
						return line.Length > 200 ? line[..200] : line;
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}

			return null;
		}
	}

	/// <summary>
	/// Registers <see cref="IFileSearchService"/>.
	/// </summary>
	public static class SearchServiceCollectionExtensions
	{
		/// <summary>
		/// Registers <see cref="IFileSearchService"/>.
		/// </summary>
		public static IServiceCollection AddLinuxSearch(this IServiceCollection services)
			=> services.AddSingleton<IFileSearchService, LinuxFileSearchService>();
	}
}
