using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;

namespace OTD.Controlls.InterlockingEnlements;

public partial class SignalTileMemory : UserControl
{
    private readonly DispatcherTimer _blinkTimer;
    private bool _blinkOnPhase;

    public static readonly StyledProperty<string> SignalNameProperty =
        AvaloniaProperty.Register<SignalTileMemory, string>(nameof(SignalName), "xxx");

    public static readonly StyledProperty<IBrush> MemoryRingBrushProperty =
        AvaloniaProperty.Register<SignalTileMemory, IBrush>(nameof(MemoryRingBrush), new SolidColorBrush(Color.Parse("#E6C200")));

    public static readonly StyledProperty<bool> MemoryBlinkingProperty =
        AvaloniaProperty.Register<SignalTileMemory, bool>(nameof(MemoryBlinking), false);

    public string SignalName { get => GetValue(SignalNameProperty); set => SetValue(SignalNameProperty, value); }
    public IBrush MemoryRingBrush { get => GetValue(MemoryRingBrushProperty); set => SetValue(MemoryRingBrushProperty, value); }
    public bool MemoryBlinking { get => GetValue(MemoryBlinkingProperty); set => SetValue(MemoryBlinkingProperty, value); }

    public SignalTileMemory()
    {
        InitializeComponent();
        _blinkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkOnPhase = !_blinkOnPhase;
            ApplyBlinkClasses();
        };
        ApplySettings();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SignalNameProperty || change.Property == MemoryRingBrushProperty || change.Property == MemoryBlinkingProperty)
        {
            ApplySettings();
        }
    }

    private void ApplySettings()
    {
        SignalNameText.Text = string.IsNullOrWhiteSpace(SignalName) ? "xxx" : SignalName;
        StorageRing.Stroke = MemoryRingBrush;

        if (MemoryBlinking)
        {
            if (!_blinkTimer.IsEnabled)
            {
                _blinkOnPhase = true;
                _blinkTimer.Start();
            }
        }
        else if (_blinkTimer.IsEnabled)
        {
            _blinkTimer.Stop();
            _blinkOnPhase = false;
        }

        ApplyBlinkClasses();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _blinkTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void ApplyBlinkClasses()
    {
        MemoryButton.Classes.Set("blinking", MemoryBlinking && _blinkOnPhase);
        MemoryButton.Classes.Set("blinking-offphase", MemoryBlinking && !_blinkOnPhase);
    }
}
