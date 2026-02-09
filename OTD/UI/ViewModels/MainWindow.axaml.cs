using Avalonia.Controls;
using Avalonia.Interactivity;
using OTD.UI.ViewModels;

namespace OTD.UI.ViewModels;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
    
    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        var newWindow = new SettingsWindow();
        // newWindow.Show(); // Öffnet das Fenster
        newWindow.ShowDialog(this); // Für ein modales Fenster
    }
    private void OpenLocoList(object sender, RoutedEventArgs e)
    {
        var newWindow = new LocoListWindow();
        // newWindow.Show(); // Öffnet das Fenster
        newWindow.ShowDialog(this); // Für ein modales Fenster
    }
}