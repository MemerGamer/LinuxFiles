using System.Collections.ObjectModel;
using System.ComponentModel;
using Files.App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UnoHost;

public sealed record PathItem(string Title);

public sealed record FileRow(string Name, string SizeText);

public sealed class SampleSidebarItem : ISidebarItemModel, ISidebarItemPresentationModel
{
    public SampleSidebarItem(string text, string iconStyleKey)
    {
        Text = text;
        IconElement = new ThemedIcon
        {
            Width = 16,
            Height = 16,
            IconType = ThemedIconTypes.Outline,
            Style = (Style)Application.Current.Resources[iconStyleKey],
        };
    }

    public string? Text { get; }
    public object? ToolTip => Text;
    public FrameworkElement? IconElement { get; }
    public FrameworkElement? ItemDecorator => null;
    public object? Children => null;
    public bool IsExpanded { get; set; }
    public string? Path => null;

    public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
}

public sealed partial class MainPage : Page
{
    public ObservableCollection<PathItem> PathItems { get; } = new()
    {
        new("home"), new("user"), new("Documents"),
    };

    public ObservableCollection<FlatSidebarItem> SidebarItems { get; } = new();

    public ObservableCollection<FileRow> Files { get; } = new();

    public bool FooterLoaded { get; set; } = true;

    public MainPage()
    {
        foreach (var (text, key) in new[]
        {
            ("Home", "App.ThemedIcons.Folder"),
            ("Documents", "App.ThemedIcons.Folder"),
            ("Downloads", "App.ThemedIcons.Copy"),
        })
        {
            SidebarItems.Add(new FlatSidebarItem(new SampleSidebarItem(text, key), 0));
        }

        for (var i = 0; i < 200; i++)
            Files.Add(new FileRow($"file_{i:000}.txt", $"{i * 3 + 1} KB"));

        InitializeComponent();
    }

    private void Tabs_AddTabButtonClick(TabView sender, object args)
        => sender.TabItems.Add(new TabViewItem { Header = $"Tab {sender.TabItems.Count + 1}" });

    private void Tabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        => sender.TabItems.Remove(args.Tab);

    private void CompactToggle_Click(object sender, RoutedEventArgs e)
        => VisualStateManager.GoToState(this, CompactToggle.IsChecked == true ? "Compact" : "Normal", true);
}
