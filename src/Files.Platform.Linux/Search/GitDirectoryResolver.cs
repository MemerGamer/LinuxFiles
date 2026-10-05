// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;

namespace Files.Platform.Linux.Search
{
	/// <summary>
	/// Locates the metadata directories of a Git working tree: <c>.git</c> may be a directory, or a gitfile
	/// (<c>gitdir: ...</c>) as used by linked worktrees and submodules, whose shared data lives in the directory named by <c>commondir</c>.
	/// </summary>
	public static class GitDirectoryResolver
	{
		/// <summary>
		/// Resolves the git directories of <paramref name="workTree"/>. Returns <see langword="null"/> when there is no usable repository.
		/// </summary>
		/// <param name="workTree">The root folder containing <c>.git</c>.</param>
		/// <returns>The per-worktree git directory (HEAD, index) and the common directory (refs, objects); both are equal for a plain repository.</returns>
		public static (string GitDir, string CommonDir)? Resolve(string workTree)
		{
			try
			{
				var dotGit = Path.Combine(workTree, ".git");
				string gitDir;
				if (Directory.Exists(dotGit))
				{
					gitDir = dotGit;
				}
				else if (File.Exists(dotGit))
				{
					string? first;
					using (var reader = new StreamReader(dotGit))
						first = reader.ReadLine();

					const string prefix = "gitdir:";
					if (first is null || !first.StartsWith(prefix, StringComparison.Ordinal))
						return null;

					gitDir = Path.GetFullPath(first[prefix.Length..].Trim(), workTree);
					if (!Directory.Exists(gitDir))
						return null;
				}
				else
				{
					return null;
				}

				var commonDir = gitDir;
				var commonFile = Path.Combine(gitDir, "commondir");
				if (File.Exists(commonFile))
				{
					var relative = File.ReadAllText(commonFile).Trim();
					if (relative.Length > 0)
					{
						var candidate = Path.GetFullPath(relative, gitDir);
						if (Directory.Exists(candidate))
							commonDir = candidate;
					}
				}

				return (gitDir, commonDir);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
			{
				return null;
			}
		}
	}
}
