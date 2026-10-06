// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Native;
using Files.Platform.Abstractions.Elevation;
using System;
using System.Collections.Generic;
using System.IO;

namespace Files.Platform.Linux.Elevation
{
	/// <summary>
	/// Resolves the few programs that are run as root to absolute paths. Only fixed system locations are considered, never <c>$PATH</c>.
	/// </summary>
	public interface ITrustedToolResolver
	{
		/// <summary>
		/// Returns the absolute path of <paramref name="name"/> (<c>pkexec</c> or <c>files-elevation-helper</c>), or null if it is not installed in a trusted location.
		/// </summary>
		string? Resolve(string name);
	}

	/// <summary>
	/// Path trust checks for privileged operations: symlinks are resolved up front and every directory on the way must be owned by root
	/// or the current user and not be writable by anyone else, so nobody else can swap a component between confirmation and execution.
	/// </summary>
	public sealed class ElevationPathChecker
	{
		private const int MaxLinkHops = 40;
		private readonly IFileOwnershipInspector inspector;
		private readonly uint userId;

		/// <summary>Creates the checker.</summary>
		public ElevationPathChecker(IFileOwnershipInspector inspector, uint userId)
		{
			this.inspector = inspector;
			this.userId = userId;
		}

		/// <summary>Gets the inspector used for all lookups.</summary>
		public IFileOwnershipInspector Inspector => inspector;

		internal uint CurrentUserId => userId;

		/// <summary>
		/// Resolves every symbolic link in <paramref name="absolutePath"/>, including the last component. Returns null for broken links or loops.
		/// </summary>
		public string? Canonicalize(string absolutePath)
		{
			var pending = new Stack<string>();
			foreach (var segment in absolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
				pending.Push(segment);

			// Stack top must be the first segment
			var ordered = new Stack<string>(pending);
			var current = new List<string>();
			var hops = 0;
			while (ordered.Count > 0)
			{
				var segment = ordered.Pop();
				if (segment == ".")
					continue;
				if (segment == "..")
				{
					if (current.Count > 0)
						current.RemoveAt(current.Count - 1);
					continue;
				}

				current.Add(segment);
				var path = "/" + string.Join('/', current);
				if (!inspector.TryGetInfo(path, out var info) || !info.IsSymbolicLink)
					continue;

				if (++hops > MaxLinkHops)
					return null;

				string? target;
				try
				{
					target = new FileInfo(path).LinkTarget;
				}
				catch (IOException)
				{
					return null;
				}

				if (string.IsNullOrEmpty(target))
					return null;

				current.RemoveAt(current.Count - 1);
				if (target.StartsWith('/'))
					current.Clear();

				var parts = target.Split('/', StringSplitOptions.RemoveEmptyEntries);
				for (var i = parts.Length - 1; i >= 0; i--)
					ordered.Push(parts[i]);
			}

			return "/" + string.Join('/', current);
		}

		/// <summary>
		/// Checks that <paramref name="canonicalDirectory"/> and all its ancestors can only be changed by root or the current user.
		/// Returns an explanation when not, otherwise null.
		/// </summary>
		public string? CheckDirectoryChain(string canonicalDirectory)
		{
			var parts = canonicalDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries);
			var path = "/";
			for (var depth = 0; depth <= parts.Length; depth++)
			{
				if (depth > 0)
					path = path == "/" ? "/" + parts[depth - 1] : path + "/" + parts[depth - 1];

				if (!inspector.TryGetInfo(path, out var info) || !info.IsDirectory)
					return $"\"{path}\" is not an accessible directory.";

				if (info.OwnerUserId != 0 && info.OwnerUserId != userId)
					return $"\"{path}\" belongs to another user, who could swap its contents before the command runs.";

				var othersWrite = (info.Mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0;
				var stickyRoot = info.OwnerUserId == 0 && (info.Mode & UnixFileMode.StickyBit) != 0;
				if (othersWrite && !stickyRoot)
					return $"\"{path}\" can be modified by other users, who could swap its contents before the command runs.";
			}

			return null;
		}
	}

	/// <summary>
	/// Resolves tools from <c>/usr/bin</c> then <c>/bin</c> and accepts them only if the file is owned by root and not writable by group or others.
	/// </summary>
	public sealed class SystemToolResolver : ITrustedToolResolver
	{
		private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "pkexec" };
		private static readonly string[] Directories = ["/usr/bin", "/bin"];
		private readonly ElevationPathChecker checker;

		/// <summary>Creates the resolver.</summary>
		public SystemToolResolver(ElevationPathChecker checker)
		{
			this.checker = checker;
		}

		/// <inheritdoc/>
		public string? Resolve(string name)
		{
			if (name == "files-elevation-helper")
				return IsRootOwnedChain(ElevationHelperProtocol.HelperPath) && IsRootOwnedChain(ElevationHelperProtocol.PolicyPath)
					&& checker.Inspector.TryGetInfo(ElevationHelperProtocol.HelperPath, out var helper)
					&& (helper.Mode & UnixFileMode.UserExecute) != 0
					? ElevationHelperProtocol.HelperPath : null;

			if (!Allowed.Contains(name))
				return null;

			foreach (var directory in Directories)
			{
				var candidate = directory + "/" + name;
				var real = checker.Canonicalize(candidate);
				if (real is null || !checker.Inspector.TryGetInfo(real, out var info) || info.IsDirectory || info.IsSymbolicLink)
					continue;

				if (IsRootOwnedChain(real, allowSetIdFile: true) && (info.Mode & UnixFileMode.UserExecute) != 0)
					return candidate;
			}

			return null;
		}

		private bool IsRootOwnedChain(string path, bool allowSetIdFile = false)
		{
			var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
			var current = "/";
			for (var index = -1; index < parts.Length; index++)
			{
				if (index >= 0) current = Path.Combine(current, parts[index]);
				if (!checker.Inspector.TryGetInfo(current, out var info) || info.IsSymbolicLink || info.OwnerUserId != 0
					|| (info.Mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0
					|| ((info.Mode & (UnixFileMode.SetUser | UnixFileMode.SetGroup)) != 0 && !(allowSetIdFile && index == parts.Length - 1))
					|| (index < parts.Length - 1 && !info.IsDirectory) || (index == parts.Length - 1 && info.IsDirectory))
					return false;
			}
			return true;
		}

	}
}
