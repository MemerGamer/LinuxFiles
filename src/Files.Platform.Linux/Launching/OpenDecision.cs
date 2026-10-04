// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Linux.Launching
{
	/// <summary>
	/// What to do when a file is activated.
	/// </summary>
	public enum OpenAction
	{
		/// <summary>Open with the default application.</summary>
		OpenDefault,

		/// <summary>Ask before running a binary (Run / Cancel).</summary>
		RunBinaryWithConfirm,

		/// <summary>Ask before running a script (Run / Display / Cancel).</summary>
		RunScriptWithConfirm,

		/// <summary>Launch a .desktop file installed in an XDG applications directory.</summary>
		LaunchDesktopTrusted,

		/// <summary>Ask (showing Name and Exec) before launching any other .desktop file.</summary>
		LaunchDesktopConfirm,
	}

	/// <summary>
	/// The kind of executable content detected in a file.
	/// </summary>
	public enum ExecutableKind
	{
		/// <summary>Not executable content.</summary>
		None,

		/// <summary>A native binary (ELF, AppImage).</summary>
		Binary,

		/// <summary>An interpreted script (shebang).</summary>
		Script,
	}

	/// <summary>
	/// Decides how to open a file. The execute bit alone never grants trust: nothing runs without confirmation,
	/// except .desktop files installed in an applications directory.
	/// </summary>
	public static class OpenDecision
	{
		/// <summary>
		/// Decides the action for a file.
		/// </summary>
		/// <param name="isDesktopEntry">The file is a parsable .desktop application entry.</param>
		/// <param name="inApplicationsDirectory">The file lives under an XDG data dir's applications directory.</param>
		/// <param name="hasExecuteBit">The (symlink-resolved) file has an execute bit.</param>
		/// <param name="kind">The detected executable content.</param>
		public static OpenAction Decide(bool isDesktopEntry, bool inApplicationsDirectory, bool hasExecuteBit, ExecutableKind kind)
		{
			if (isDesktopEntry)
				return inApplicationsDirectory ? OpenAction.LaunchDesktopTrusted : OpenAction.LaunchDesktopConfirm;

			if (!hasExecuteBit)
				return OpenAction.OpenDefault;

			return kind switch
			{
				ExecutableKind.Binary => OpenAction.RunBinaryWithConfirm,
				ExecutableKind.Script => OpenAction.RunScriptWithConfirm,
				_ => OpenAction.OpenDefault,
			};
		}
	}
}
