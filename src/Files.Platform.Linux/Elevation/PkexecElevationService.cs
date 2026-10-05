// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Elevation;
using Files.Platform.Linux.Launching;
using Files.Platform.Linux.Native;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Elevation
{
	/// <summary>
	/// Runs a program and reports how it ended. Exists as a seam so tests never start the real <c>pkexec</c>.
	/// </summary>
	public interface IElevatedProcessRunner
	{
		/// <summary>
		/// Runs <paramref name="fileName"/> with <paramref name="arguments"/> (no shell) and waits for it.
		/// </summary>
		Task<(int ExitCode, string StandardError)> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
	}

	/// <summary>
	/// Starts real processes.
	/// </summary>
	public sealed class ProcessElevatedRunner : IElevatedProcessRunner
	{
		/// <inheritdoc/>
		public async Task<(int ExitCode, string StandardError)> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
		{
			var startInfo = new ProcessStartInfo(fileName)
			{
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				RedirectStandardInput = true,
				CreateNoWindow = true,
			};

			foreach (var argument in arguments)
				startInfo.ArgumentList.Add(argument);

			using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The process could not be started.");
			process.StandardInput.Close();
			var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
			var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

			try
			{
				await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
				throw;
			}

			await stdout.ConfigureAwait(false);
			return (process.ExitCode, await stderr.ConfigureAwait(false));
		}
	}

	/// <summary>
	/// Runs privileged file operations through <c>pkexec</c> (polkit shows the authentication dialog).
	/// Only fixed programs from trusted system locations run (<c>rm</c>, <c>cp</c>), with an argument vector and never a shell. Every path
	/// is canonicalized and its directories are checked first, existing targets are never replaced, copies belong to root and a plan is
	/// re-validated right before it runs.
	/// </summary>
	public sealed class PkexecElevationService : IElevationService
	{
		// pkexec: 126 = the user dismissed the dialog or is not authorized; 127 = authentication failed or the command could not be started
		private const int PkexecNotAuthorized = 126;

		private readonly ElevationPathChecker checker;
		private readonly ITrustedToolResolver tools;
		private readonly IElevatedProcessRunner runner;

		/// <summary>
		/// Creates the service using the real file system inspection and tool locations.
		/// </summary>
		public PkexecElevationService(IElevatedProcessRunner runner)
			: this(new ElevationPathChecker(new StatxFileOwnershipInspector(), ProcessIdentityNative.CurrentUserId), null, runner)
		{
		}

		/// <summary>
		/// Creates the service. <paramref name="tools"/> defaults to the system locations.
		/// </summary>
		public PkexecElevationService(ElevationPathChecker checker, ITrustedToolResolver? tools, IElevatedProcessRunner runner)
		{
			this.checker = checker;
			this.tools = tools ?? new SystemToolResolver(checker);
			this.runner = runner;
		}

		/// <inheritdoc/>
		public bool IsAvailable => tools.Resolve("pkexec") is not null && tools.Resolve("rm") is not null && tools.Resolve("cp") is not null && tools.Resolve("mv") is not null;

		/// <inheritdoc/>
		public ElevatedPlanResult PlanDelete(IReadOnlyList<string> paths)
		{
			if (paths.Count == 0)
				return ElevatedPlanResult.Refuse("Nothing to delete.");

			var resolved = ResolveSources(paths, out var refusal);
			if (resolved is null)
				return ElevatedPlanResult.Refuse(refusal);

			if (tools.Resolve("rm") is not { } rm)
				return ElevatedPlanResult.Refuse("rm was not found in a trusted location.");

			var sources = resolved.Select(s => s.Path).ToList();
			var arguments = new List<string> { "-rf", "--" };
			arguments.AddRange(sources);
			return ElevatedPlanResult.Ok(new ElevatedPlan(ElevatedOperation.Delete, sources, null, [new ElevatedCommand(rm, arguments)]));
		}

		/// <inheritdoc/>
		public ElevatedPlanResult PlanCopy(IReadOnlyList<string> sources, string destinationFolder)
			=> PlanCopyCore(sources, destinationFolder, ElevatedOperation.Copy);

		/// <inheritdoc/>
		public ElevatedPlanResult PlanMove(IReadOnlyList<string> sources, string destinationFolder)
		{
			var copy = PlanCopyCore(sources, destinationFolder, ElevatedOperation.Move);
			if (copy.Plan is null)
				return copy;

			var delete = PlanDelete(copy.Plan.Sources);
			if (delete.Plan is null)
				return delete;

			// A move is copy, then delete: the copies belong to root and no setuid/ownership of the original can carry over
			return ElevatedPlanResult.Ok(copy.Plan with { Commands = [.. copy.Plan.Commands, .. delete.Plan.Commands] });
		}

		/// <inheritdoc/>
		public ElevatedPlanResult PlanRename(string path, string newName)
		{
			if (!IsPlainName(newName))
				return ElevatedPlanResult.Refuse("The new name is not a plain file name.");

			var resolved = ResolveSources([path], out var refusal);
			if (resolved is null)
				return ElevatedPlanResult.Refuse(refusal);

			if (tools.Resolve("mv") is not { } mv)
				return ElevatedPlanResult.Refuse("mv was not found in a trusted location.");

			var source = resolved[0];
			var target = source.Parent + "/" + newName;
			if (Exists(target))
				return ElevatedPlanResult.Refuse("An item with that name already exists.");

			// -n and -T: never replace, and never treat the target as a folder to move into
			return ElevatedPlanResult.Ok(new ElevatedPlan(ElevatedOperation.Rename, [source.Path], newName, [new ElevatedCommand(mv, ["-n", "-T", "--", source.Path, target])]));
		}

		/// <inheritdoc/>
		public async Task<ElevatedResult> RunAsync(ElevatedPlan plan, CancellationToken cancellationToken = default)
		{
			var rebuilt = Rebuild(plan);
			if (rebuilt.Plan is null)
				return Failure(rebuilt.Refusal);

			if (!Same(rebuilt.Plan, plan))
				return Failure("The operation no longer matches what was confirmed.");

			if (tools.Resolve("pkexec") is not { } pkexec)
				return Failure("pkexec was not found in a trusted location.");

			for (var index = 0; index < plan.Commands.Count; index++)
			{
				var command = plan.Commands[index];
				var full = new List<string>(command.Arguments.Count + 1) { command.Program };
				full.AddRange(command.Arguments);

				ElevatedResult result;
				try
				{
					var (exitCode, error) = await runner.RunAsync(pkexec, full, cancellationToken).ConfigureAwait(false);
					result = new ElevatedResult(exitCode == 0, exitCode == PkexecNotAuthorized, exitCode, error);
				}
				catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
				{
					return Failure(ex.Message);
				}

				if (!result.Succeeded)
					return result;

				// A move deletes the originals only after the copies are verified
				if (plan.Operation == ElevatedOperation.Move && index == 0 && !CopiesExist(plan))
					return Failure("The items were not copied (a target may already exist).");
			}

			return Verify(plan) ? new ElevatedResult(true, false, 0, string.Empty) : Failure("The operation did not take effect (a target may already exist).");
		}

		private sealed record Source(string Path, string Parent, string Leaf);

		private ElevatedPlanResult PlanCopyCore(IReadOnlyList<string> sources, string destinationFolder, ElevatedOperation operation)
		{
			if (sources.Count == 0)
				return ElevatedPlanResult.Refuse("Nothing to copy.");

			var resolved = ResolveSources(sources, out var refusal);
			if (resolved is null)
				return ElevatedPlanResult.Refuse(refusal);

			if (!IsSafeAbsolutePath(destinationFolder) || checker.Canonicalize(Normalize(destinationFolder)) is not { } destination)
				return ElevatedPlanResult.Refuse("The destination must be an existing absolute folder.");

			if (checker.CheckDirectoryChain(destination) is { } untrusted)
				return ElevatedPlanResult.Refuse(untrusted);

			var leaves = new HashSet<string>(StringComparer.Ordinal);
			foreach (var source in resolved)
			{
				if (!leaves.Add(source.Leaf))
					return ElevatedPlanResult.Refuse("Two items have the same name.");
				if (destination == source.Path || destination.StartsWith(source.Path + "/", StringComparison.Ordinal))
					return ElevatedPlanResult.Refuse("An item cannot be copied into itself.");
				if (Exists(destination == "/" ? "/" + source.Leaf : destination + "/" + source.Leaf))
					return ElevatedPlanResult.Refuse($"\"{source.Leaf}\" already exists in the destination.");
				if (HasSetIdBits(source.Path))
					return ElevatedPlanResult.Refuse($"\"{source.Leaf}\" contains setuid or setgid files.");
			}

			if (tools.Resolve("cp") is not { } cp)
				return ElevatedPlanResult.Refuse("cp was not found in a trusted location.");

			// -n never replaces (and never writes through a planted symlink); without -p/-a the copies belong to root and keep no setid bits
			var arguments = new List<string> { "-R", "-P", "-n", "--preserve=timestamps,links", "--" };
			arguments.AddRange(resolved.Select(s => s.Path));
			arguments.Add(destination == "/" ? "/" : destination + "/");
			return ElevatedPlanResult.Ok(new ElevatedPlan(operation, resolved.Select(s => s.Path).ToList(), destination, [new ElevatedCommand(cp, arguments)]));
		}

		// Canonical parent directory (symlinks resolved, ancestors trusted) plus the untouched leaf, for each existing path
		private List<Source>? ResolveSources(IReadOnlyList<string> paths, out string refusal)
		{
			refusal = string.Empty;
			var result = new List<Source>(paths.Count);
			foreach (var path in paths)
			{
				if (!IsSafeAbsolutePath(path) || Normalize(path) == "/")
				{
					refusal = "The path is not a usable absolute path.";
					return null;
				}

				var normalized = Normalize(path);
				var leaf = Path.GetFileName(normalized);
				var parent = checker.Canonicalize(Path.GetDirectoryName(normalized) ?? "/");
				if (parent is null || leaf.Length == 0)
				{
					refusal = "The folder could not be resolved.";
					return null;
				}

				if (checker.CheckDirectoryChain(parent) is { } untrusted)
				{
					refusal = untrusted;
					return null;
				}

				var resolved = (parent == "/" ? "/" : parent + "/") + leaf;
				if (!Exists(resolved))
				{
					refusal = $"\"{leaf}\" does not exist.";
					return null;
				}

				result.Add(new Source(resolved, parent, leaf));
			}

			if (result.Select(s => s.Path).Distinct(StringComparer.Ordinal).Count() != result.Count)
			{
				refusal = "The same item is listed twice.";
				return null;
			}

			return result;
		}

		private ElevatedPlanResult Rebuild(ElevatedPlan plan) => plan.Operation switch
		{
			ElevatedOperation.Delete => PlanDelete(plan.Sources),
			ElevatedOperation.Copy when plan.Target is not null => PlanCopy(plan.Sources, plan.Target),
			ElevatedOperation.Move when plan.Target is not null => PlanMove(plan.Sources, plan.Target),
			ElevatedOperation.Rename when plan.Target is not null && plan.Sources.Count == 1 => PlanRename(plan.Sources[0], plan.Target),
			_ => ElevatedPlanResult.Refuse("The operation is incomplete."),
		};

		private static bool Same(ElevatedPlan a, ElevatedPlan b)
			=> a.Operation == b.Operation
			&& a.Commands.Count == b.Commands.Count
			&& a.Commands.Zip(b.Commands).All(pair => pair.First.Program == pair.Second.Program && pair.First.Arguments.SequenceEqual(pair.Second.Arguments));

		private bool CopiesExist(ElevatedPlan plan)
			=> plan.Target is { } target && plan.Sources.All(source => Exists((target == "/" ? "/" : target + "/") + Path.GetFileName(source)));

		private bool Verify(ElevatedPlan plan) => plan.Operation switch
		{
			ElevatedOperation.Delete => plan.Sources.All(source => !Exists(source)),
			ElevatedOperation.Copy => CopiesExist(plan),
			ElevatedOperation.Move => CopiesExist(plan) && plan.Sources.All(source => !Exists(source)),
			ElevatedOperation.Rename => plan.Target is { } name && Exists(Path.GetDirectoryName(plan.Sources[0])! + "/" + name) && !Exists(plan.Sources[0]),
			_ => false,
		};

		// lstat: a dangling symlink counts as existing
		private bool Exists(string path) => checker.Inspector.TryGetInfo(path, out _);

		private bool HasSetIdBits(string path)
		{
			var pending = new Stack<string>();
			pending.Push(path);
			var visited = 0;
			while (pending.Count > 0)
			{
				var current = pending.Pop();
				if (++visited > 1_000_000 || !checker.Inspector.TryGetInfo(current, out var info))
					continue;

				if ((info.Mode & (UnixFileMode.SetUser | UnixFileMode.SetGroup)) != 0 && !info.IsDirectory)
					return true;

				if (!info.IsDirectory || info.IsSymbolicLink)
					continue;

				try
				{
					foreach (var child in Directory.EnumerateFileSystemEntries(current))
						pending.Push(child);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					// An unreadable folder cannot be inspected; cp running as root would read it, so refuse
					return true;
				}
			}

			return false;
		}

		private static ElevatedResult Failure(string message) => new(false, false, -1, message);

		private static bool IsSafeAbsolutePath(string path) => !string.IsNullOrEmpty(path) && path.StartsWith('/') && !path.Contains('\0');

		private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('/') is { Length: > 0 } full ? full : "/";

		private static bool IsPlainName(string name)
			=> !string.IsNullOrEmpty(name) && name is not ("." or "..") && name.IndexOfAny(['/', '\0']) < 0;
	}
}
