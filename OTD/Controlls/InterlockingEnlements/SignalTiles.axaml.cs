using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;

namespace OTD.Controlls.InterlockingEnlements;

public partial class SignalTiles : UserControl
{
    private readonly DispatcherTimer _blinkTimer;
    private bool _blinkOnPhase;

    public static readonly StyledProperty<string> SignalNameProperty =
        AvaloniaProperty.Register<SignalTiles, string>(nameof(SignalName), "xxx");

    public static readonly StyledProperty<bool> HasMemoryProperty =
        AvaloniaProperty.Register<SignalTiles, bool>(nameof(HasMemory), true);

    public static readonly StyledProperty<IBrush> MemoryRingBrushProperty =
        AvaloniaProperty.Register<SignalTiles, IBrush>(nameof(MemoryRingBrush), new SolidColorBrush(Color.Parse("#E6C200")));

    public static readonly StyledProperty<bool> MemoryBlinkingProperty =
        AvaloniaProperty.Register<SignalTiles, bool>(nameof(MemoryBlinking), false);

    public string SignalName
    {
        get => GetValue(SignalNameProperty);
        set => SetValue(SignalNameProperty, value);
    }

    public bool HasMemory
    {
        get => GetValue(HasMemoryProperty);
        set => SetValue(HasMemoryProperty, value);
    }

    public IBrush MemoryRingBrush
    {
        get => GetValue(MemoryRingBrushProperty);
        set => SetValue(MemoryRingBrushProperty, value);
    }

    public bool MemoryBlinking
    {
        get => GetValue(MemoryBlinkingProperty);
        set => SetValue(MemoryBlinkingProperty, value);
    }

    public SignalTiles()
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
        if (change.Property == SignalNameProperty ||
            change.Property == HasMemoryProperty ||
            change.Property == MemoryRingBrushProperty ||
            change.Property == MemoryBlinkingProperty)
        {
            ApplySettings();
        }
    }

    private void ApplySettings()
    {
        if (SignalNameText is null || MemoryGroup is null || StorageRing is null || MemoryButton is null || NoMemoryButton is null)
        {
            return;
        }

        SignalNameText.Text = string.IsNullOrWhiteSpace(SignalName) ? "xxx" : SignalName;
        MemoryGroup.IsVisible = HasMemory;
        NoMemoryButton.IsVisible = !HasMemory;
        StorageRing.Stroke = MemoryRingBrush;

        var shouldBlink = HasMemory && MemoryBlinking;
        if (shouldBlink)
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
        if (MemoryButton is null)
        {
            return;
        }

        var shouldBlink = HasMemory && MemoryBlinking;
        MemoryButton.Classes.Set("blinking", shouldBlink && _blinkOnPhase);
        MemoryButton.Classes.Set("blinking-offphase", shouldBlink && !_blinkOnPhase);
    }
}
