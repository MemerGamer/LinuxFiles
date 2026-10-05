// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class OpenInIDEAction : ObservableObject, IAction
	{
		private readonly IDevToolsSettingsService _devToolsSettingsService;

		private readonly IContentPageContext _context;

		public string Label
			=> string.Format(
				Strings.OpenInIDE.GetLocalizedResource(),
				_devToolsSettingsService.IDEName);

		public string Description
			=> string.Format(
				Strings.OpenInIDEDescription.GetLocalizedResource(),
				_devToolsSettingsService.IDEName);

		public ActionCategory Category
			=> ActionCategory.Open;

		public bool IsExecutable =>
			_context.Folder is not null &&
			!string.IsNullOrWhiteSpace(_devToolsSettingsService.IDEPath);

		public OpenInIDEAction()
		{
			_devToolsSettingsService = Ioc.Default.GetRequiredService<IDevToolsSettingsService>();
			_context = Ioc.Default.GetRequiredService<IContentPageContext>();
			_context.PropertyChanged += Context_PropertyChanged;
			_devToolsSettingsService.PropertyChanged += DevSettings_PropertyChanged;
		}

		public async Task ExecuteAsync(object? parameter = null)
		{
			var workingDirectory = _context.ShellPage?.ShellViewModel?.WorkingDirectory;

			if (OperatingSystem.IsLinux())
			{
				if (!SystemIO.Path.IsPathRooted(workingDirectory))
					return;

				var launched = await Ioc.Default.GetRequiredService<Files.Platform.Abstractions.Launching.IExecutableService>()
					.StartAsync(_devToolsSettingsService.IDEPath, [workingDirectory]);
				if (!launched)
					await DynamicDialogFactory.ShowFor_IDEErrorDialog(_devToolsSettingsService.IDEName);
				return;
			}

#if WINDOWS
			var res = await Win32Helper.RunPowershellCommandAsync(
				$"& {Win32Helper.ToPowerShellStringLiteral(_devToolsSettingsService.IDEPath)} {Win32Helper.ToPowerShellStringLiteral(workingDirectory)}",
				PowerShellExecutionOptions.Hidden
			);

			if (!res)
				await DynamicDialogFactory.ShowFor_IDEErrorDialog(_devToolsSettingsService.IDEName);
#endif
		}

		private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(IContentPageContext.Folder))
				OnPropertyChanged(nameof(IsExecutable));
		}

		private void DevSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(IDevToolsSettingsService.IDEPath))
			{
				OnPropertyChanged(nameof(IsExecutable));
			}
			else if (e.PropertyName == nameof(IDevToolsSettingsService.IDEName))
			{
				OnPropertyChanged(nameof(Label));
				OnPropertyChanged(nameof(Description));
			}
		}
	}
}
