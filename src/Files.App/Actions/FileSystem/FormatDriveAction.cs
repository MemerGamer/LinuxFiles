// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal partial class FormatDriveAction : ObservableObject, IAction
	{
		private readonly IContentPageContext context = Ioc.Default.GetRequiredService<IContentPageContext>();

		private readonly DrivesViewModel drivesViewModel = Ioc.Default.GetRequiredService<DrivesViewModel>();

		public string Label
			=> Strings.FormatDriveText.GetLocalizedResource();

		public string Description
			=> Strings.FormatDriveDescription.GetLocalizedResource();

		public virtual ActionCategory Category
			=> ActionCategory.FileSystem;

		public virtual bool IsExecutable =>
			OperatingSystem.IsWindows() &&
			context.HasItem &&
			!context.HasSelection &&
			drivesViewModel.Drives
				.Cast<DriveItem>()
				.FirstOrDefault(x => string.Equals(x.Path, context.Folder?.ItemPath)) is DriveItem driveItem &&
				!(driveItem.Type == DriveType.Network || string.Equals(context.Folder?.ItemPath, $@"{Constants.UserEnvironmentPaths.SystemDrivePath}\", StringComparison.OrdinalIgnoreCase)) &&
				IsFormatAvailable(driveItem.Path);

		public virtual bool IsAccessibleGlobally
			=> true;

		public FormatDriveAction()
		{
			context.PropertyChanged += Context_PropertyChanged;
		}

		public virtual Task ExecuteAsync(object? parameter = null)
		{
			return FormatDrive(context.Folder?.ItemPath);
		}

		// Windows opens the shell format dialog; Linux opens GNOME Disks or KDE Partition Manager when one is installed (else the command is hidden)
		protected static bool IsFormatAvailable(string? path)
#if !WINDOWS
			=> DriveHelpers.CanFormat(path);
#else
			=> true;
#endif

		protected static Task FormatDrive(string? path)
#if !WINDOWS
			=> DriveHelpers.OpenFormatDialogAsync(path);
#else
			=> Win32Helper.OpenFormatDriveDialog(path ?? string.Empty);
#endif

		public void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName is nameof(IContentPageContext.HasItem))
				OnPropertyChanged(nameof(IsExecutable));
		}
	}
}
