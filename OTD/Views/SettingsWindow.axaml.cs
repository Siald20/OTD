using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace OTD.Views;

public partial class SettingsWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _settingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OTD",
        "settings.json");

    private AppSettings _settings = new();

    public SettingsWindow()
    {
        InitializeComponent();

        _settings = LoadSettings();
        DataContext = _settings;
        ZentraleList.ItemsSource = _settings.Zentralen;
        ApplyTheme(_settings.ThemeIndex);
        UpdateZentraleState();
    }

    private void ThemeSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ThemeSelector.SelectedIndex < 0)
        {
            return;
        }

        _settings.ThemeIndex = ThemeSelector.SelectedIndex;
        ApplyTheme(_settings.ThemeIndex);
    }

    private void AddZentrale_OnClick(object? sender, RoutedEventArgs e)
    {
        var number = _settings.Zentralen.Count + 1;

        _settings.Zentralen.Add(new ZentraleSettings
        {
            Name = $"Zentrale {number}",
            Type = "Roco Z21",
            Host = "192.168.0.111",
            Port = 21105,
            IsEnabled = true
        });

        SaveStatusText.Text = "Neue Zentrale angelegt. Noch nicht gespeichert.";
        UpdateZentraleState();
    }

    private void SaveSettings_OnClick(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
    }

    private AppSettings LoadSettings()
    {
        if (!File.Exists(_settingsFilePath))
        {
            SaveStatusText.Text = "Noch keine settings.json vorhanden.";
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.EnsureDefaults();
            SaveStatusText.Text = $"Geladen: {_settingsFilePath}";
            return settings;
        }
        catch (JsonException)
        {
            SaveStatusText.Text = "settings.json konnte nicht gelesen werden. Standardwerte aktiv.";
            return new AppSettings();
        }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);

        var json = JsonSerializer.Serialize(_settings, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);

        SaveStatusText.Text = $"Gespeichert: {_settingsFilePath}";
    }

    private void UpdateZentraleState()
    {
        var count = _settings.Zentralen.Count;
        EmptyZentraleText.IsVisible = count == 0;
        ZentraleCountText.Text = count switch
        {
            0 => "Keine Zentrale angelegt",
            1 => "1 Zentrale angelegt",
            _ => $"{count} Zentralen angelegt"
        };
    }

    private static void ApplyTheme(int themeIndex)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = themeIndex switch
        {
            1 => ThemeVariant.Light,
            2 => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}

public sealed class AppSettings : NotifySettingsObject
{
    private int _themeIndex;
    private bool _startMaximized = true;
    private bool _loadLastProject = true;

    public int ThemeIndex
    {
        get => _themeIndex;
        set => SetField(ref _themeIndex, value);
    }

    public bool StartMaximized
    {
        get => _startMaximized;
        set => SetField(ref _startMaximized, value);
    }

    public bool LoadLastProject
    {
        get => _loadLastProject;
        set => SetField(ref _loadLastProject, value);
    }

    public ObservableCollection<ZentraleSettings> Zentralen { get; set; } = [];

    public OperationSettings Operation { get; set; } = new();

    public TrackPlanSettings TrackPlan { get; set; } = new();

    public RouteSettings Routes { get; set; } = new();

    public LocoControlSettings Loco { get; set; } = new();

    public SafetySettings Safety { get; set; } = new();

    public void EnsureDefaults()
    {
        Zentralen ??= [];
        Operation ??= new OperationSettings();
        TrackPlan ??= new TrackPlanSettings();
        Routes ??= new RouteSettings();
        Loco ??= new LocoControlSettings();
        Safety ??= new SafetySettings();
    }
}

public sealed class OperationSettings : NotifySettingsObject
{
    private bool _autoConnect = true;
    private bool _useSimulation;
    private decimal _commandIntervalMs = 100;
    private decimal _defaultSpeedPercent = 35;

    public bool AutoConnect
    {
        get => _autoConnect;
        set => SetField(ref _autoConnect, value);
    }

    public bool UseSimulation
    {
        get => _useSimulation;
        set => SetField(ref _useSimulation, value);
    }

    public decimal CommandIntervalMs
    {
        get => _commandIntervalMs;
        set => SetField(ref _commandIntervalMs, value);
    }

    public decimal DefaultSpeedPercent
    {
        get => _defaultSpeedPercent;
        set => SetField(ref _defaultSpeedPercent, value);
    }
}

public sealed class TrackPlanSettings : NotifySettingsObject
{
    private decimal _gridSize = 24;
    private bool _snapToGrid = true;
    private bool _showBlockLabels = true;
    private decimal _autoSaveMinutes = 5;

    public decimal GridSize
    {
        get => _gridSize;
        set => SetField(ref _gridSize, value);
    }

    public bool SnapToGrid
    {
        get => _snapToGrid;
        set => SetField(ref _snapToGrid, value);
    }

    public bool ShowBlockLabels
    {
        get => _showBlockLabels;
        set => SetField(ref _showBlockLabels, value);
    }

    public decimal AutoSaveMinutes
    {
        get => _autoSaveMinutes;
        set => SetField(ref _autoSaveMinutes, value);
    }
}

public sealed class RouteSettings : NotifySettingsObject
{
    private int _searchModeIndex = 1;
    private bool _allowOccupiedSymbols;
    private bool _allowLockedSymbols;
    private decimal _maxVisitedSymbols = 500;

    public int SearchModeIndex
    {
        get => _searchModeIndex;
        set => SetField(ref _searchModeIndex, value);
    }

    public bool AllowOccupiedSymbols
    {
        get => _allowOccupiedSymbols;
        set => SetField(ref _allowOccupiedSymbols, value);
    }

    public bool AllowLockedSymbols
    {
        get => _allowLockedSymbols;
        set => SetField(ref _allowLockedSymbols, value);
    }

    public decimal MaxVisitedSymbols
    {
        get => _maxVisitedSymbols;
        set => SetField(ref _maxVisitedSymbols, value);
    }
}

public sealed class LocoControlSettings : NotifySettingsObject
{
    private int _defaultSpeedStepsIndex = 2;
    private decimal _accelerationDelayMs = 250;
    private bool _functionsAreMomentary;
    private bool _warnBeforeDirectionChange = true;

    public int DefaultSpeedStepsIndex
    {
        get => _defaultSpeedStepsIndex;
        set => SetField(ref _defaultSpeedStepsIndex, value);
    }

    public decimal AccelerationDelayMs
    {
        get => _accelerationDelayMs;
        set => SetField(ref _accelerationDelayMs, value);
    }

    public bool FunctionsAreMomentary
    {
        get => _functionsAreMomentary;
        set => SetField(ref _functionsAreMomentary, value);
    }

    public bool WarnBeforeDirectionChange
    {
        get => _warnBeforeDirectionChange;
        set => SetField(ref _warnBeforeDirectionChange, value);
    }
}

public sealed class SafetySettings : NotifySettingsObject
{
    private bool _emergencyStopOnDisconnect = true;
    private bool _confirmRouteActivation = true;
    private decimal _maxActiveTrains = 8;
    private decimal _shortCircuitCutoffMs = 250;

    public bool EmergencyStopOnDisconnect
    {
        get => _emergencyStopOnDisconnect;
        set => SetField(ref _emergencyStopOnDisconnect, value);
    }

    public bool ConfirmRouteActivation
    {
        get => _confirmRouteActivation;
        set => SetField(ref _confirmRouteActivation, value);
    }

    public decimal MaxActiveTrains
    {
        get => _maxActiveTrains;
        set => SetField(ref _maxActiveTrains, value);
    }

    public decimal ShortCircuitCutoffMs
    {
        get => _shortCircuitCutoffMs;
        set => SetField(ref _shortCircuitCutoffMs, value);
    }
}

public sealed class ZentraleSettings : NotifySettingsObject
{
    private string _name = string.Empty;
    private string _type = string.Empty;
    private string _host = string.Empty;
    private decimal _port;
    private bool _isEnabled;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Type
    {
        get => _type;
        set => SetField(ref _type, value);
    }

    public string Host
    {
        get => _host;
        set => SetField(ref _host, value);
    }

    public decimal Port
    {
        get => _port;
        set => SetField(ref _port, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }
}

public abstract class NotifySettingsObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
