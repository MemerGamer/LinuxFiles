// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Elevation;
using Files.Platform.Linux.Launching;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Files.Platform.Linux.Elevation
{
	/// <summary>
	/// Holds the plan a confirmation dialog shows and hands out exactly that plan, once, when the user confirms.
	/// The plan is recomputed on every input change; <see cref="Confirm"/> only succeeds for the plan whose display text is on screen,
	/// a plan that cannot be displayed completely never confirms, and nothing changes after the dialog was confirmed or closed.
	/// Not thread-safe: call it from the UI thread.
	/// </summary>
	public sealed class ElevationPlanPreview
	{
		private readonly Func<string, ElevatedPlanResult> planner;
		private ElevatedPlan? plan;
		private bool finished;

		/// <summary>Creates the preview. <paramref name="planner"/> builds the plan for the current input.</summary>
		public ElevationPlanPreview(Func<string, ElevatedPlanResult> planner)
		{
			this.planner = planner;
		}

		/// <summary>Gets the text to display for the current plan, or the reason it is refused.</summary>
		public string DisplayText { get; private set; } = string.Empty;

		/// <summary>Gets whether the confirm button may be enabled.</summary>
		public bool CanConfirm => !finished && plan is not null;

		/// <summary>Recomputes the plan for <paramref name="input"/>. Ignored once confirmed or closed (late updates).</summary>
		public void Update(string input)
		{
			if (finished)
				return;

			var result = planner(input);
			var text = result.Plan is { } candidate ? Format(candidate) : null;

			// A plan that cannot be shown completely is never offered
			plan = text is null || result.Plan is null ? null : result.Plan with
			{
				Sources = Array.AsReadOnly(result.Plan.Sources.ToArray()),
				Commands = Array.AsReadOnly(result.Plan.Commands.Select(command => command with { Arguments = Array.AsReadOnly(command.Arguments.ToArray()) }).ToArray()),
			};
			DisplayText = text ?? (result.Plan is null ? DisplaySanitizer.Field(result.Refusal, 400) : "Too many items to display.");
		}

		/// <summary>
		/// Returns the displayed plan if <paramref name="shownText"/> is what is on screen for it, and finishes the preview, so a second
		/// confirm (double click, Enter) or a later update cannot produce another plan. Returns null otherwise.
		/// </summary>
		public ElevatedPlan? Confirm(string shownText)
		{
			if (!CanConfirm || !string.Equals(shownText, DisplayText, StringComparison.Ordinal))
				return null;

			finished = true;
			return plan;
		}

		/// <summary>Ends the preview without confirming (cancel, close, Escape).</summary>
		public void Close() => finished = true;

		/// <summary>
		/// Renders every command: <c>pkexec</c>, the program and each argument escaped on its own line; commands are separated by a blank line.
		/// Returns null when the plan is too large to be shown in full.
		/// </summary>
		public static string? Format(ElevatedPlan plan)
		{
			var blocks = new List<string>();
			foreach (var command in plan.Commands)
			{
				IReadOnlyList<string> arguments = command.Arguments;
				if (command.Program == ElevationHelperProtocol.HelperPath && arguments.Count == 1)
				{
					try
					{
						var request = ElevationHelperProtocol.ParseRequest(arguments[0]);
						arguments = [.. HelperAuthorization.Arguments(arguments[0]), "stdin (JSON)", HelperAuthorization.DisplayJson(request)];
					}
					catch (Exception) { return null; }
				}
				var lines = DisplaySanitizer.FullArguments(["pkexec", command.Program, .. arguments]);
				if (lines is null)
					return null;

				blocks.Add(string.Join(Environment.NewLine, lines));
			}

			return string.Join(Environment.NewLine + Environment.NewLine, blocks);
		}
	}
}
