using Avalonia.Controls;
using OTD.Views;
using System.Threading.Tasks;

namespace OTD;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        await Task.Delay(2000);
        ShowMainPage();
    }

    private void ShowMainPage()
    {
        RootContent.Content = new MainPage(ShowTrackPlanPage);
    }

    private void ShowTrackPlanPage()
    {
        RootContent.Content = new TrackPlanPage(ShowMainPage);
    }
}
