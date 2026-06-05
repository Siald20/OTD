using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace OTD;

public partial class MainWindow : Window
{
    private ComboBox _stationSelector = null!;
    private ComboBox _commandStationSelector = null!;
    private TextBox _hostTextBox = null!;
    private TextBox _portTextBox = null!;
    private CheckBox _autoConnectCheckBox = null!;
    private CheckBox _restoreLayoutCheckBox = null!;
    private TextBlock _statusText = null!;

    public MainWindow()
    {
        InitializeComponent();
        _stationSelector = this.FindControl<ComboBox>("StationSelector")!;
        _commandStationSelector = this.FindControl<ComboBox>("CommandStationSelector")!;
        _hostTextBox = this.FindControl<TextBox>("HostTextBox")!;
        _portTextBox = this.FindControl<TextBox>("PortTextBox")!;
        _autoConnectCheckBox = this.FindControl<CheckBox>("AutoConnectCheckBox")!;
        _restoreLayoutCheckBox = this.FindControl<CheckBox>("RestoreLayoutCheckBox")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void SaveSettings(object? sender, RoutedEventArgs e)
    {
        var station = SelectedText(_stationSelector);
        var commandStation = SelectedText(_commandStationSelector);
        _statusText.Text = $"Gespeichert: {station}, {commandStation}, {_hostTextBox.Text}:{_portTextBox.Text}";
    }

    private void ResetSettings(object? sender, RoutedEventArgs e)
    {
        _stationSelector.SelectedIndex = 0;
        _commandStationSelector.SelectedIndex = 0;
        _hostTextBox.Text = "127.0.0.1";
        _portTextBox.Text = "11082";
        _autoConnectCheckBox.IsChecked = false;
        _restoreLayoutCheckBox.IsChecked = true;
        _statusText.Text = "Standardeinstellungen wiederhergestellt";
    }

    private static string SelectedText(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString() ?? string.Empty
            : comboBox.SelectedItem?.ToString() ?? string.Empty;
    }
}
