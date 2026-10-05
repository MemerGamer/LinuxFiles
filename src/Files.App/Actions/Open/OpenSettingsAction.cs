// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class OpenSettingsAction : BaseUIAction, IAction
	{
		private readonly IContentPageContext context = Ioc.Default.GetRequiredService<IContentPageContext>();

		public string Label
			=> Strings.Settings.GetLocalizedResource();

		public string Description
			=> Strings.OpenSettingsDescription.GetLocalizedResource();

		public ActionCategory Category
			=> ActionCategory.Open;

		public HotKey HotKey
			=> new(Keys.OemComma, KeyModifiers.Ctrl);

		public RichGlyph Glyph
			=> new(themedIconStyle: "App.ThemedIcons.Settings");

		public Task ExecuteAsync(object? parameter = null)
		{
			var settingsPage = (parameter as SettingsNavigationParams)?.PageKind.ToString();

			return settingsPage is not null
				? NavigationHelpers.AddNewTabByParamAsync(
					typeof(ShellPanesPage),
					new PaneNavigationArguments()
					{
						LeftPaneNavPathParam = "Settings",
						LeftPaneSelectItemParam = settingsPage,
					})
				: OpenOrFocusSettingsTab();
		}

		private static Task OpenOrFocusSettingsTab()
		{
			var existingIndex = MainPageViewModel.AppInstances.ToList().FindIndex(tab =>
				tab.NavigationParameter?.NavigationParameter switch
				{
					string path => path == "Settings",
					PaneNavigationArguments args => args.LeftPaneNavPathParam == "Settings" && args.RightPaneNavPathParam is null,
					_ => false
				});

			if (existingIndex < 0)
				return NavigationHelpers.OpenPathInNewTab("Settings", true);

			App.AppModel.TabStripSelectedIndex = existingIndex;
			return Task.CompletedTask;
		}
	}
}