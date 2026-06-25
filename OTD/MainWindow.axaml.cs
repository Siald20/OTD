using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace OTD;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void ExitApplication(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenWsrTest(object? sender, RoutedEventArgs e)
    {
        new WsrTestWindow().Show(this);
    }

    private void OpenBlockTest(object? sender, RoutedEventArgs e)
    {
        new BlockTestWindow().Show(this);
    }

    private void OpenRelayPlan(object? sender, RoutedEventArgs e)
    {
        new RelayPlanWindow().Show(this);
    }
}
