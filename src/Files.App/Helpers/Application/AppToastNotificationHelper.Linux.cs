namespace Files.App.Helpers.Application
{
	/// <summary>
	/// Desktop (Linux) variant of the toast helper; uses notify-send when available.
	/// </summary>
	internal static class AppToastNotificationHelper
	{
		public static void ShowUnhandledExceptionToast()
			=> Notify(Strings.ExceptionNotificationHeader.GetLocalizedResource(), Strings.ExceptionNotificationBody.GetLocalizedResource());

		public static void ShowBackgroundRunningToast()
			=> Notify(Strings.BackgroundRunningNotificationHeader.GetLocalizedResource(), Strings.BackgroundRunningNotificationBody.GetLocalizedResource());

		public static void ShowDriveEjectToast()
			=> Notify(Strings.EjectNotificationHeader.GetLocalizedResource(), Strings.EjectNotificationBody.GetLocalizedResource());

		private static void Notify(string title, string body)
		{
			// LINUX-TODO(notifications): use org.freedesktop.Notifications over D-Bus (or the Linux launcher service) instead of spawning notify-send
			try
			{
				var psi = new System.Diagnostics.ProcessStartInfo("notify-send")
				{
					UseShellExecute = false,
					CreateNoWindow = true,
				};
				psi.ArgumentList.Add("--app-name=Files");
				psi.ArgumentList.Add(title);
				psi.ArgumentList.Add(body);
				System.Diagnostics.Process.Start(psi);
			}
			catch
			{
				// notify-send missing; ignore
			}
		}
	}
}
