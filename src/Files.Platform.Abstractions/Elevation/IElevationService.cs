// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Elevation
{
	/// <summary>
	/// The result of an elevated command.
	/// </summary>
	/// <param name="Succeeded">Whether the command ran, exited with code 0 and its effect was verified.</param>
	/// <param name="WasDismissed">Whether the user dismissed the authentication prompt or is not authorized.</param>
	/// <param name="ExitCode">The exit code, or -1 if the command did not run.</param>
	/// <param name="Error">The standard error output or the reason, if any.</param>
	public sealed record ElevatedResult(bool Succeeded, bool WasDismissed, int ExitCode, string Error);

	/// <summary>
	/// A privileged operation.
	/// </summary>
	public enum ElevatedOperation
	{
		/// <summary>Delete items.</summary>
		Delete,

		/// <summary>Copy items into a folder.</summary>
		Copy,

		/// <summary>Move items into a folder (copy, then delete the originals).</summary>
		Move,

		/// <summary>Rename one item in place.</summary>
		Rename,
	}

	/// <summary>
	/// One privileged command: the absolute program and its argument vector. Nothing is ever passed through a shell.
	/// </summary>
	public sealed record ElevatedCommand(string Program, IReadOnlyList<string> Arguments);

	/// <summary>
	/// A validated operation: the commands that will run, in order, with the inputs they were derived from.
	/// Show <see cref="Commands"/> to the user, then pass this very object to <see cref="IElevationService.RunAsync"/>.
	/// </summary>
	/// <param name="Operation">The operation.</param>
	/// <param name="Sources">The items acted on (absolute paths).</param>
	/// <param name="Target">The destination folder (copy, move) or the new name (rename).</param>
	/// <param name="Commands">The exact commands, each run through <c>pkexec</c>.</param>
	public sealed record ElevatedPlan(ElevatedOperation Operation, IReadOnlyList<string> Sources, string? Target, IReadOnlyList<ElevatedCommand> Commands);

	/// <summary>
	/// The outcome of planning: a plan, or the reason the operation is refused.
	/// </summary>
	public sealed record ElevatedPlanResult(ElevatedPlan? Plan, string Refusal)
	{
		/// <summary>Creates a successful result.</summary>
		public static ElevatedPlanResult Ok(ElevatedPlan plan) => new(plan, string.Empty);

		/// <summary>Creates a refusal.</summary>
		public static ElevatedPlanResult Refuse(string reason) => new(null, reason);
	}

	/// <summary>
	/// Runs privileged file operations after authenticating the user.
	/// </summary>
	public interface IElevationService
	{
		/// <summary>
		/// Gets a value indicating whether elevation is available on this system.
		/// </summary>
		bool IsAvailable { get; }

		/// <summary>Plans deleting <paramref name="paths"/> as root in one command.</summary>
		ElevatedPlanResult PlanDelete(IReadOnlyList<string> paths);

		/// <summary>Plans copying <paramref name="sources"/> into <paramref name="destinationFolder"/>. Existing items are never replaced and the copies belong to root.</summary>
		ElevatedPlanResult PlanCopy(IReadOnlyList<string> sources, string destinationFolder);

		/// <summary>Plans moving <paramref name="sources"/> into <paramref name="destinationFolder"/> as copy, then delete.</summary>
		ElevatedPlanResult PlanMove(IReadOnlyList<string> sources, string destinationFolder);

		/// <summary>Plans renaming <paramref name="path"/> to <paramref name="newName"/> (a name, never a path). An existing item is never replaced.</summary>
		ElevatedPlanResult PlanRename(string path, string newName);

		/// <summary>
		/// Runs exactly the commands of <paramref name="plan"/>. The plan is re-derived and re-validated first; a plan that differs from what
		/// the service would build now (forged or stale) is refused. The effect is verified afterwards.
		/// </summary>
		Task<ElevatedResult> RunAsync(ElevatedPlan plan, CancellationToken cancellationToken = default);
	}
}
