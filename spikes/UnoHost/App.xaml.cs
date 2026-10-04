using Microsoft.UI.Xaml;

namespace UnoHost;

public partial class App : Application
{
    public App() => InitializeComponent();

    protected Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window { Title = "Files.App.Controls on Uno" };
        MainWindow.Content = new MainPage();
        MainWindow.Activate();
    }
}
