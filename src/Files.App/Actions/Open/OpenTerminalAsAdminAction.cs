// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class OpenTerminalAsAdminAction : OpenTerminalAction
	{
		public override string Label
#if WINDOWS
			=> Strings.OpenTerminalAsAdmin.GetLocalizedResource();
#else
			=> Strings.OpenTerminalAsAdminLinux.GetLocalizedResource();
#endif

		public override string Description
			=> Strings.OpenTerminalAsAdminDescription.GetLocalizedResource();

		public override HotKey HotKey
			=> new(Keys.Oem3, KeyModifiers.CtrlShift);

		public override bool IsExecutable
			=> OperatingSystem.IsWindows() && base.IsExecutable;

		protected override ProcessStartInfo? GetProcessStartInfo(string[] paths)
		{
			var startInfo = base.GetProcessStartInfo(paths);
			if (startInfo is not null)
			{
				startInfo.Verb = "runas";
				startInfo.UseShellExecute = true;
			}

			return startInfo;
		}
	}
}
