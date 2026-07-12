using Avalonia;
using Avalonia.Controls;

namespace OTD.Controls.InterlockingEnlements;

public partial class SignalTileNoMemory : UserControl
{
    public static readonly StyledProperty<string> SignalNameProperty =
        AvaloniaProperty.Register<SignalTileNoMemory, string>(nameof(SignalName), "xxx");

    public string SignalName { get => GetValue(SignalNameProperty); set => SetValue(SignalNameProperty, value); }

    public SignalTileNoMemory()
    {
        InitializeComponent();
        ApplySettings();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SignalNameProperty)
        {
            ApplySettings();
        }
    }

    private void ApplySettings()
    {
        SignalNameText.Text = string.IsNullOrWhiteSpace(SignalName) ? "xxx" : SignalName;
    }
}
