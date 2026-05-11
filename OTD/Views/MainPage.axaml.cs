using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;

namespace OTD.Views;

public partial class MainPage : UserControl
{
    private readonly Action? _openTrackPlan;
    private SettingsWindow? _settingsWindow;

    public MainPage()
        : this(null)
    {
    }

    public MainPage(Action? openTrackPlan)
    {
        _openTrackPlan = openTrackPlan;
        InitializeComponent();
    }

    private void OpenTrackPlan_OnClick(object? sender, RoutedEventArgs e)
    {
        _openTrackPlan?.Invoke();
    }

    private void OpenSettings(object? sender, RoutedEventArgs e)
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;

        if (this.GetVisualRoot() is Window owner)
        {
            _settingsWindow.Show(owner);
            return;
        }

        _settingsWindow.Show();
    }
}
