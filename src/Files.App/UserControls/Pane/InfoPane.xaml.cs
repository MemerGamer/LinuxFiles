// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinRT;

namespace Files.App.UserControls
{
	public enum PreviewPanePositions : ushort
	{
		None,
		Right,
		Bottom,
	}

	public sealed partial class InfoPane : UserControl
	{
		public PreviewPanePositions Position { get; private set; } = PreviewPanePositions.None;

		private readonly IInfoPaneSettingsService PaneSettingsService;

		private readonly ICommandManager Commands;

		private IContentPageContext contentPageContext { get; } = Ioc.Default.GetRequiredService<IContentPageContext>();

		public InfoPaneViewModel ViewModel { get; private set; }

		private ObservableContext Context { get; } = new();

		public InfoPane()
		{
			InitializeComponent();
			PaneSettingsService = Ioc.Default.GetRequiredService<IInfoPaneSettingsService>();
			Commands = Ioc.Default.GetRequiredService<ICommandManager>();
			ViewModel = Ioc.Default.GetRequiredService<InfoPaneViewModel>();
#if DESKTOP
			// Uno doesn't evaluate the enum-valued IsEqualStateTrigger states in the XAML, so drive them explicitly.
			ViewModel.PropertyChanged += ViewModel_PropertyChanged;
			Loaded += (_, _) => UpdateStateGroups();
#endif
		}

#if DESKTOP
		private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName is nameof(InfoPaneViewModel.PreviewPaneState) or nameof(InfoPaneViewModel.SelectedTab))
				UpdateStateGroups();
		}

		private void UpdateStateGroups()
		{
			VisualStateManager.GoToState(this, ViewModel.PreviewPaneState.ToString(), false);
			VisualStateManager.GoToState(this, ViewModel.SelectedTab == InfoPaneTabs.Preview ? "PreviewTab" : "DetailsTab", false);
		}
#endif

		public void UpdatePosition(double panelWidth, double panelHeight)
		{
			if (panelWidth > 700)
			{
				Position = PreviewPanePositions.Right;
				(MinWidth, MinHeight) = (150, 0);
				VisualStateManager.GoToState(this, "Vertical", true);
			}
			else
			{
				Position = PreviewPanePositions.Bottom;
				(MinWidth, MinHeight) = (0, 140);
				VisualStateManager.GoToState(this, "Horizontal", true);
			}
		}

		private void Root_Unloaded(object sender, RoutedEventArgs e)
		{
#if DESKTOP
			ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
#endif
			ViewModel.UnloadPreview();
			PreviewControlPresenter.Content = null;
			Bindings.StopTracking();
		}

		private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
			=> Context.IsHorizontal = Root.ActualWidth >= Root.ActualHeight;

		private void MenuFlyoutItem_Tapped(object sender, TappedRoutedEventArgs e)
			=> ViewModel?.UpdateSelectedItemPreviewAsync(true);

		[DynamicWindowsRuntimeCast(typeof(UserControl))]
		private void FileTag_PointerEntered(object sender, PointerRoutedEventArgs e)
		{
			VisualStateManager.GoToState((UserControl)sender, "PointerOver", true);
		}

		[DynamicWindowsRuntimeCast(typeof(UserControl))]
		private void FileTag_PointerExited(object sender, PointerRoutedEventArgs e)
		{
			VisualStateManager.GoToState((UserControl)sender, "Normal", true);
		}

		private sealed partial class ObservableContext : ObservableObject
		{
			private bool isHorizontal = false;
			public bool IsHorizontal
			{
				get => isHorizontal;
				set => SetProperty(ref isHorizontal, value);
			}
		}

		[DynamicWindowsRuntimeCast(typeof(TextBlock))]
		[DynamicWindowsRuntimeCast(typeof(StackPanel))]
		private void TagItem_Tapped(object sender, TappedRoutedEventArgs e)
		{
			var tagName = ((sender as StackPanel)?.Children[1] as TextBlock)?.Text;
			if (tagName is null)
				return;

			contentPageContext.ShellPage?.SubmitSearch(FolderSearch.FormatTagQuery(tagName));
		}
	}
}