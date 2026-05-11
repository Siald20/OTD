using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.VisualTree;
using OTD.TrackPlan;
using OTD.TrackPlan.Interlocking;
using OTD.TrackPlan.Interlocking.Profiles;
using System;
using System.Collections.Generic;
using System.Linq;
using IOPath = System.IO.Path;

namespace OTD.Views;

public partial class TrackPlanPage : UserControl
{
    private readonly Action? _navigateBack;

    private readonly List<PaletteElement> _paletteElements =
    [
        new(TrackSymbolKind.Signal, TrackEditorTool.Signal, "Signal"),
        new(TrackSymbolKind.Track, TrackEditorTool.Track, "Gleis"),
        new(TrackSymbolKind.TrackBlock, TrackEditorTool.TrackBlock, "Block"),
        new(TrackSymbolKind.Switch, TrackEditorTool.SwitchLeftRightUp, "W L-R oben"),
        new(TrackSymbolKind.Switch, TrackEditorTool.SwitchLeftRightDown, "W L-R unten"),
        new(TrackSymbolKind.Switch, TrackEditorTool.SwitchRightLeftUp, "W R-L oben"),
        new(TrackSymbolKind.Switch, TrackEditorTool.SwitchRightLeftDown, "W R-L unten"),
        new(TrackSymbolKind.DoubleSlipSwitch, TrackEditorTool.DoubleSlipSwitch, "DKW"),
        new(TrackSymbolKind.LevelCrossing, TrackEditorTool.LevelCrossing, "Bue"),
        new(TrackSymbolKind.Uncoupler, TrackEditorTool.Uncoupler, "Entkuppler"),
        new(TrackSymbolKind.Platform, TrackEditorTool.Platform, "Bahnsteig"),
        new(TrackSymbolKind.BufferStop, TrackEditorTool.BufferStop, "Prellbock"),
        new(TrackSymbolKind.TextLabel, TrackEditorTool.TextLabel, "Text")
    ];

    private readonly TrackPlanDocumentStore _trackPlanDocumentStore = new();
    private readonly RouteDocumentStore _routeDocumentStore = new();
    private readonly string _planFilePath = IOPath.Combine(Environment.CurrentDirectory, "plan.xml");
    private readonly string _routesFilePath = IOPath.Combine(Environment.CurrentDirectory, "Routes.xml");
    private readonly TrackPlanEditorModel _trackPlanEditor;
    private readonly RouteBuilder _routeBuilder = new();
    private readonly RouteInterlockingService _interlocking = new(new Domino67InterlockingProfile());
    private readonly HashSet<string> _highlightedConnectionKeys = [];
    private readonly HashSet<string> _activeRouteConnectionKeys = [];
    private readonly HashSet<string> _releasedRouteConnectionKeys = [];
    private readonly HashSet<string> _occupiedSymbolIds = [];
    private readonly HashSet<string> _greenSignalIds = [];
    private readonly HashSet<string> _lockedSymbolIds = [];
    private readonly List<RouteResult> _visibleRoutes = [];
    private readonly List<RouteResult> _activeRoutes = [];
    private SettingsWindow? _settingsWindow;
    private TrackEditorTool _activeTool = TrackEditorTool.Select;
    private DrawnTrackSymbol? _selectedOperationSymbol;
    private DrawnTrackSymbol? _operationStartSignal;
    private RouteResult? _selectedOperationRoute;
    private DrawnTrackSymbol? _connectionStart;
    private SymbolPort? _connectionStartPort;
    private DrawnTrackSymbol? _draggedSymbol;
    private Point _dragOffset;
    private bool _isDragging;
    private bool _isOperationMode;

    public TrackPlanPage()
        : this(null)
    {
    }

    public TrackPlanPage(Action? navigateBack)
    {
        _navigateBack = navigateBack;
        _trackPlanEditor = new TrackPlanEditorModel(LoadTrackPlanDocument());
        InitializeComponent();
        RenderPalette();
        LoadSavedRoutes();
        if (_trackPlanEditor.Document.Symbols.Count == 0)
        {
            LoadDemoTrackPlan();
        }
        else
        {
            TrackPlanStatus.Text = $"Gleisplan aus {IOPath.GetFileName(_planFilePath)} geladen.";
            RenderTrackPlan();
        }
    }

    private void BackToMain_OnClick(object? sender, RoutedEventArgs e)
    {
        _navigateBack?.Invoke();
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

    private void LoadDemoTrackPlan_OnClick(object? sender, RoutedEventArgs e)
    {
        LoadDemoTrackPlan();
    }

    private void SaveXml_OnClick(object? sender, RoutedEventArgs e)
    {
        SaveTrackPlan();
        SaveRoutes();
        TrackPlanStatus.Text = $"{IOPath.GetFileName(_planFilePath)} und {IOPath.GetFileName(_routesFilePath)} gespeichert.";
    }

    private TrackPlanDocument LoadTrackPlanDocument()
    {
        try
        {
            return _trackPlanDocumentStore.Load(_planFilePath);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"plan.xml konnte nicht geladen werden: {exception.Message}");
            return new TrackPlanDocument();
        }
    }

    private void SaveTrackPlan()
    {
        try
        {
            _trackPlanDocumentStore.Save(_planFilePath, _trackPlanEditor.Document);
        }
        catch (Exception exception)
        {
            TrackPlanStatus.Text = $"plan.xml konnte nicht gespeichert werden: {exception.Message}";
        }
    }

    private void LoadSavedRoutes()
    {
        try
        {
            var graph = _trackPlanEditor.ToGraph();
            _visibleRoutes.Clear();
            _visibleRoutes.AddRange(_routeDocumentStore.Load(_routesFilePath, graph));
            RefreshRouteLists();
        }
        catch (Exception exception)
        {
            TrackPlanStatus.Text = $"Routes.xml konnte nicht geladen werden: {exception.Message}";
        }
    }

    private void SaveRoutes()
    {
        try
        {
            _routeDocumentStore.Save(_routesFilePath, _visibleRoutes);
        }
        catch (Exception exception)
        {
            TrackPlanStatus.Text = $"Routes.xml konnte nicht gespeichert werden: {exception.Message}";
        }
    }

    private void ClearSavedRoutes()
    {
        _visibleRoutes.Clear();
        _activeRoutes.Clear();
        _activeRouteConnectionKeys.Clear();
        _releasedRouteConnectionKeys.Clear();
        _greenSignalIds.Clear();
        _lockedSymbolIds.Clear();
        UpdateActiveRouteText();
        RefreshRouteLists();
        SaveRoutes();
    }

    private void SaveTrackPlanAndClearRoutes()
    {
        SaveTrackPlan();
        ClearSavedRoutes();
    }

    private void SetEditorMode_OnClick(object? sender, RoutedEventArgs e)
    {
        _isOperationMode = false;
        _selectedOperationSymbol = null;
        _operationStartSignal = null;
        TrackCanvas.Background = GetThemeBrush("TrackBedBrush", Brushes.LightGray);
        EditorPanel.IsVisible = true;
        EditorAutomationPanel.IsVisible = true;
        OperationPanel.IsVisible = false;
        EditorModeButton.Classes.Set("primary", true);
        OperationModeButton.Classes.Set("primary", false);
        TrackPlanStatus.Text = "Editor aktiv. Elemente koennen platziert, verschoben und verbunden werden.";
        RenderTrackPlan();
    }

    private void SetOperationMode_OnClick(object? sender, RoutedEventArgs e)
    {
        _isOperationMode = true;
        _activeTool = TrackEditorTool.Select;
        _connectionStart = null;
        _connectionStartPort = null;
        _operationStartSignal = null;
        TrackCanvas.Background = IltisBackgroundBrush;
        EditorPanel.IsVisible = false;
        EditorAutomationPanel.IsVisible = false;
        OperationPanel.IsVisible = true;
        EditorModeButton.Classes.Set("primary", false);
        OperationModeButton.Classes.Set("primary", true);
        if (_visibleRoutes.Count == 0)
        {
            PopulateRoutes();
            TrackPlanStatus.Text = "Bedienung aktiv. Startsignal anklicken, danach Zielsignal anklicken.";
        }
        else
        {
            RefreshRouteLists();
            TrackPlanStatus.Text = $"Bedienung aktiv. Startsignal anklicken, danach Zielsignal anklicken.";
        }

        RenderPalette();
        RenderTrackPlan();
    }

    private void TrackCanvas_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isOperationMode)
        {
            return;
        }

        if (!e.GetCurrentPoint(TrackCanvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = e.GetPosition(TrackCanvas);

        if (TryGetKindForTool(_activeTool, out var kind))
        {
            var symbol = _trackPlanEditor.AddSymbol(kind, Snap(position.X), Snap(position.Y));
            ApplyToolDefaults(symbol, _activeTool);
            TrackPlanStatus.Text = $"{symbol.Name} {symbol.Id} erstellt.";
            SaveTrackPlanAndClearRoutes();
            RenderTrackPlan();
        }
    }

    private void FindRoute_OnClick(object? sender, RoutedEventArgs e)
    {
        _highlightedConnectionKeys.Clear();

        var signals = _trackPlanEditor.Document.Symbols
            .Where(symbol => symbol.Kind is TrackSymbolKind.Signal)
            .ToList();

        if (signals.Count < 2)
        {
            TrackPlanStatus.Text = "Mindestens zwei Signale werden fuer eine Fahrstrasse benoetigt.";
            RenderTrackPlan();
            return;
        }

        var graph = _trackPlanEditor.ToGraph();
        var result = _routeBuilder.FindRoute(graph, signals.First().Id, signals.Last().Id);

        if (!result.IsSuccess || result.Route is null)
        {
            TrackPlanStatus.Text = result.Failure?.Message ?? "Keine Fahrstrasse gefunden.";
            RenderTrackPlan();
            return;
        }

        foreach (var connection in result.Route.Connections)
        {
            _highlightedConnectionKeys.Add(GetConnectionKey(connection.FromSymbolId, connection.ToSymbolId));
            _highlightedConnectionKeys.Add(GetConnectionKey(connection.ToSymbolId, connection.FromSymbolId));
        }

        var switches = result.Route.SwitchCommands.Count == 0
            ? "keine Weichen"
            : string.Join(", ", result.Route.SwitchCommands.Select(command => $"{command.SwitchName} -> {command.Position}"));

        TrackPlanStatus.Text = $"Fahrstrasse {result.Route.StartSignal.Name} -> {result.Route.TargetSignal.Name} gefunden; {switches}.";
        RenderTrackPlan();
    }

    private void FindAllRoutes_OnClick(object? sender, RoutedEventArgs e)
    {
        PopulateRoutes();
    }

    private void PopulateRoutes()
    {
        _highlightedConnectionKeys.Clear();
        _visibleRoutes.Clear();

        var graph = _trackPlanEditor.ToGraph();
        _visibleRoutes.AddRange(_routeBuilder.FindAllRoutes(graph));
        RefreshRouteLists();
        SaveRoutes();

        if (_visibleRoutes.Count == 0)
        {
            TrackPlanStatus.Text = "Keine Fahrstrassen gefunden.";
            RenderTrackPlan();
            return;
        }

        HighlightRoute(_visibleRoutes[0]);
        RouteList.SelectedIndex = 0;
        OperationRouteList.SelectedIndex = 0;
        _selectedOperationRoute = _visibleRoutes[0];
        TrackPlanStatus.Text = $"{_visibleRoutes.Count} Fahrstrassen gefunden. Waehle eine Route in der Liste.";
    }

    private void RefreshRouteLists()
    {
        RouteList.Items.Clear();
        OperationRouteList.Items.Clear();

        foreach (var route in _visibleRoutes)
        {
            var label = $"{route.StartSignal.Name} -> {route.TargetSignal.Name} ({route.Connections.Count} Segmente)";
            RouteList.Items.Add(label);
            OperationRouteList.Items.Add(label);
        }

        if (_visibleRoutes.Count == 0)
        {
            _selectedOperationRoute = null;
            return;
        }

        RouteList.SelectedIndex = 0;
        OperationRouteList.SelectedIndex = 0;
        _selectedOperationRoute = _visibleRoutes[0];
    }

    private void RouteList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (RouteList.SelectedIndex < 0 || RouteList.SelectedIndex >= _visibleRoutes.Count)
        {
            return;
        }

        HighlightRoute(_visibleRoutes[RouteList.SelectedIndex]);
    }

    private void OperationRouteList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (OperationRouteList.SelectedIndex < 0 || OperationRouteList.SelectedIndex >= _visibleRoutes.Count)
        {
            _selectedOperationRoute = null;
            return;
        }

        _selectedOperationRoute = _visibleRoutes[OperationRouteList.SelectedIndex];
        HighlightRoute(_selectedOperationRoute);
    }

    private void SetRoute_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedOperationRoute is null)
        {
            TrackPlanStatus.Text = "Keine Fahrstrasse ausgewaehlt.";
            return;
        }

        SetSelectedOperationRoute();
    }

    private void SetSelectedOperationRoute()
    {
        if (_selectedOperationRoute is null)
        {
            TrackPlanStatus.Text = "Keine Fahrstrasse ausgewaehlt.";
            return;
        }

        // Die Route wurde vorher nur gesucht. Erst hier entscheidet die Stellwerkslogik,
        // ob sie betrieblich gestellt werden darf und welche Elementzustände zu setzen sind.
        var settingResult = _interlocking.TrySetRoute(new RouteSettingRequest
        {
            Route = _selectedOperationRoute,
            Document = _trackPlanEditor.Document,
            OccupiedSymbolIds = _occupiedSymbolIds,
            ActiveRoutes = _activeRoutes,
            LockedSymbolIds = _lockedSymbolIds
        });

        if (!settingResult.IsSuccess)
        {
            TrackPlanStatus.Text = settingResult.Message;
            RenderTrackPlan();
            return;
        }

        // Ab hier war das Stellen erfolgreich. Die UI uebernimmt nur noch das Ergebnis:
        // aktive Fahrwegmarkierung, Signalbegriffe und Statusmeldung.
        _activeRoutes.Add(_selectedOperationRoute);
        _releasedRouteConnectionKeys.Clear();
        foreach (var connection in _selectedOperationRoute.Connections)
        {
            _activeRouteConnectionKeys.Add(GetConnectionKey(connection.FromSymbolId, connection.ToSymbolId));
            _activeRouteConnectionKeys.Add(GetConnectionKey(connection.ToSymbolId, connection.FromSymbolId));
        }

        foreach (var symbolId in settingResult.LockedSymbolIds)
        {
            _lockedSymbolIds.Add(symbolId);
        }

        foreach (var signalId in settingResult.GreenSignalIds)
        {
            _greenSignalIds.Add(signalId);
        }

        UpdateActiveRouteText();
        TrackPlanStatus.Text = settingResult.Message + " Weichenbefehle: " + FormatSwitchCommands(settingResult.SwitchCommands);
        SaveTrackPlan();
        RenderTrackPlan();
    }

    private void ReleaseRoute_OnClick(object? sender, RoutedEventArgs e)
    {
        _activeRoutes.Clear();
        _activeRouteConnectionKeys.Clear();
        _releasedRouteConnectionKeys.Clear();
        _greenSignalIds.Clear();
        _lockedSymbolIds.Clear();
        _operationStartSignal = null;
        UpdateActiveRouteText();
        TrackPlanStatus.Text = "Fahrstrasse aufgeloest.";
        RenderTrackPlan();
    }

    private void ToggleSelectedBlock_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedOperationSymbol is null || _selectedOperationSymbol.Kind is not TrackSymbolKind.TrackBlock)
        {
            TrackPlanStatus.Text = "Waehle zuerst in der Bedienebene einen Streckenblock aus.";
            return;
        }

        if (!_occupiedSymbolIds.Add(_selectedOperationSymbol.Id))
        {
            _occupiedSymbolIds.Remove(_selectedOperationSymbol.Id);
            ReleaseActiveRouteElement(_selectedOperationSymbol);
            TrackPlanStatus.Text = $"{_selectedOperationSymbol.Name} ist frei.";
        }
        else
        {
            var affectedRoutes = _activeRoutes
                .Where(route => route.Symbols.Any(symbol => symbol.Id == _selectedOperationSymbol.Id))
                .ToList();
            foreach (var route in affectedRoutes)
            {
                _greenSignalIds.Remove(route.StartSignal.Id);
            }

            TrackPlanStatus.Text = $"{_selectedOperationSymbol.Name} ist belegt.";
        }

        RenderTrackPlan();
    }

    private void EmergencyStop_OnClick(object? sender, RoutedEventArgs e)
    {
        _activeRoutes.Clear();
        _activeRouteConnectionKeys.Clear();
        _releasedRouteConnectionKeys.Clear();
        _greenSignalIds.Clear();
        _lockedSymbolIds.Clear();
        _highlightedConnectionKeys.Clear();
        _operationStartSignal = null;
        UpdateActiveRouteText();
        TrackPlanStatus.Text = "Not-Halt ausgeloest.";
        RenderTrackPlan();
    }

    private void SetTool(TrackEditorTool tool, string status)
    {
        _activeTool = tool;
        _connectionStart = null;
        TrackPlanStatus.Text = status;
        RenderPalette();
        RenderTrackPlan();
    }

    private void RenderPalette()
    {
        ElementPalette.Children.Clear();

        foreach (var element in _paletteElements)
        {
            var item = new Border
            {
                Margin = new Thickness(0, 0, 8, 8),
                Child = CreatePaletteElement(element.Kind, element.Label),
                Tag = element
            };

            item.PointerPressed += PaletteElement_OnPointerPressed;
            StylePaletteItem(item, element.Tool);
            ElementPalette.Children.Add(item);
        }
    }

    private Border CreatePaletteElement(TrackSymbolKind kind, string name)
    {
        return new Border
        {
            Background = GetSymbolBackground(kind),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8),
            Child = new StackPanel
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = GetSymbolIcon(kind),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        FontSize = 16,
                        FontWeight = FontWeight.Bold,
                        Foreground = GetThemeBrush("TrackPlanSymbolTextBrush", Brushes.Black)
                    },
                    new TextBlock
                    {
                        Text = name,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        FontSize = 11,
                        Foreground = GetThemeBrush("TrackPlanSymbolTextBrush", Brushes.Black)
                    }
                }
            }
        };
    }

    private void StylePaletteItem(Border border, TrackEditorTool tool)
    {
        border.Height = 64;
        border.CornerRadius = new CornerRadius(8);
        border.BorderThickness = new Thickness(2);
        border.BorderBrush = _activeTool == tool
            ? GetThemeBrush("TrackPlanSymbolSelectedBorderBrush", Brushes.DeepSkyBlue)
            : GetThemeBrush("TrackPlanSymbolBorderBrush", Brushes.Gray);
        border.Background = GetThemeBrush("PanelRaisedBrush", Brushes.Transparent);
    }

    private void PaletteElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: PaletteElement element })
        {
            return;
        }

        SetTool(element.Tool, $"{element.Label}-Werkzeug aktiv. Klicke in den Gleisplan.");
    }

    private void LoadDemoTrackPlan()
    {
        _trackPlanEditor.Document.Symbols.Clear();
        _trackPlanEditor.Document.Connections.Clear();
        _highlightedConnectionKeys.Clear();
        _activeRouteConnectionKeys.Clear();
        _releasedRouteConnectionKeys.Clear();
        _occupiedSymbolIds.Clear();
        _greenSignalIds.Clear();
        _lockedSymbolIds.Clear();
        _activeRoutes.Clear();
        _visibleRoutes.Clear();
        RouteList.Items.Clear();
        OperationRouteList.Items.Clear();
        _selectedOperationRoute = null;
        _selectedOperationSymbol = null;

        var signalA = _trackPlanEditor.AddSymbol(TrackSymbolKind.Signal, 80, 220, "Signal A");
        signalA.SignalDirection = SignalDirection.LeftToRight;
        var switch1 = _trackPlanEditor.AddSymbol(TrackSymbolKind.Switch, 250, 220, "Weiche 1");
        switch1.Properties[SwitchOrientationProperty] = SwitchOrientation.LeftRightUp;
        var track1 = _trackPlanEditor.AddSymbol(TrackSymbolKind.TrackBlock, 430, 160, "Block 1");
        var track2 = _trackPlanEditor.AddSymbol(TrackSymbolKind.TrackBlock, 430, 280, "Block 2");
        var dkw = _trackPlanEditor.AddSymbol(TrackSymbolKind.DoubleSlipSwitch, 540, 220, "DKW 1");
        var signalB = _trackPlanEditor.AddSymbol(TrackSymbolKind.Signal, 620, 160, "Signal B");
        signalB.SignalDirection = SignalDirection.LeftToRight;
        var signalC = _trackPlanEditor.AddSymbol(TrackSymbolKind.Signal, 620, 280, "Signal C");
        signalC.SignalDirection = SignalDirection.LeftToRight;

        _trackPlanEditor.Connect(signalA.Id, "out", switch1.Id, "A");
        _trackPlanEditor.Connect(switch1.Id, "B", track1.Id, "left");
        _trackPlanEditor.Connect(track1.Id, "right", dkw.Id, "A");
        _trackPlanEditor.Connect(dkw.Id, "D", signalC.Id, "in");
        _trackPlanEditor.Connect(switch1.Id, "C", track2.Id, "left");
        _trackPlanEditor.Connect(track2.Id, "right", dkw.Id, "C");
        _trackPlanEditor.Connect(dkw.Id, "B", signalB.Id, "in");

        _activeTool = TrackEditorTool.Select;
        _connectionStart = null;
        ActiveRouteText.Text = "Keine Fahrstrasse aktiv.";
        TrackCanvas.Background = _isOperationMode
            ? IltisBackgroundBrush
            : GetThemeBrush("TrackBedBrush", Brushes.LightGray);
        TrackPlanStatus.Text = "Demo-Gleisplan geladen. Fahrstrasse suchen nutzt erstes bis letztes Signal.";
        SaveTrackPlanAndClearRoutes();
        RenderTrackPlan();
    }

    private void RenderTrackPlan()
    {
        TrackCanvas.Children.Clear();

        foreach (var connection in _trackPlanEditor.Document.Connections)
        {
            var from = FindSymbol(connection.FromSymbolId);
            var to = FindSymbol(connection.ToSymbolId);
            if (from is null || to is null)
            {
                continue;
            }

            var connectionKey = GetConnectionKey(from.Id, to.Id);
            var isReleased = _releasedRouteConnectionKeys.Contains(connectionKey);
            var isActive = _activeRouteConnectionKeys.Contains(connectionKey);
            var isHighlighted = _highlightedConnectionKeys.Contains(connectionKey);
            var line = new Line
            {
                StartPoint = GetPortPoint(from, connection.FromPort),
                EndPoint = GetPortPoint(to, connection.ToPort),
                Stroke = isReleased
                    ? GetThemeBrush("TrackPlanRailBrush", Brushes.DimGray)
                    : isActive
                    ? GetThemeBrush("SignalGreenBrush", Brushes.LimeGreen)
                    : isHighlighted
                    ? GetThemeBrush("TrackPlanRouteHighlightBrush", Brushes.DeepSkyBlue)
                    : GetThemeBrush("TrackPlanRailBrush", Brushes.DimGray),
                StrokeThickness = isActive || isHighlighted ? 8 : 5,
                StrokeLineCap = PenLineCap.Round
            };

            if (!_isOperationMode)
            {
                line.ContextMenu = CreateConnectionContextMenu(connection);
                line.PointerPressed += Connection_OnPointerPressed;
                line.Tag = connection;
            }

            if (_isOperationMode)
            {
                ApplyIltisConnectionStyle(line, isReleased, isActive, isHighlighted);
            }

            TrackCanvas.Children.Add(line);
        }

        foreach (var symbol in _trackPlanEditor.Document.Symbols)
        {
            TrackCanvas.Children.Add(CreateSymbolControl(symbol));
        }

        if (!_isOperationMode)
        {
            foreach (var symbol in _trackPlanEditor.Document.Symbols)
            {
                AddPortControls(symbol);
            }
        }
    }

    private Control CreateSymbolControl(DrawnTrackSymbol symbol)
    {
        if (_isOperationMode)
        {
            return CreateOperationSymbolControl(symbol);
        }

        var border = new Border
        {
            Width = 92,
            Height = 58,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            BorderBrush = symbol == _selectedOperationSymbol
                ? GetThemeBrush("SignalGreenBrush", Brushes.LimeGreen)
                : symbol == _connectionStart
                ? GetThemeBrush("TrackPlanSymbolSelectedBorderBrush", Brushes.DeepSkyBlue)
                : GetThemeBrush("TrackPlanSymbolBorderBrush", Brushes.Gray),
            Background = GetSymbolBackground(symbol.Kind, symbol.Id),
            Tag = symbol,
            Child = new StackPanel
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = GetSymbolIcon(symbol),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        FontSize = 18,
                        FontWeight = FontWeight.Bold,
                        Foreground = GetThemeBrush("TrackPlanSymbolTextBrush", Brushes.Black)
                    },
                    new TextBlock
                    {
                        Text = GetSymbolCaption(symbol),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        FontSize = 12,
                        Foreground = GetThemeBrush("TrackPlanSymbolTextBrush", Brushes.Black)
                    }
                }
            }
        };

        if (!_isOperationMode)
        {
            border.ContextMenu = CreateSymbolContextMenu(symbol);
        }
        border.PointerPressed += Symbol_OnPointerPressed;
        Canvas.SetLeft(border, symbol.X - border.Width / 2);
        Canvas.SetTop(border, symbol.Y - border.Height / 2);
        return border;
    }

    private void AddPortControls(DrawnTrackSymbol symbol)
    {
        foreach (var portName in GetPortNames(symbol))
        {
            var point = GetPortPoint(symbol, portName);
            var isSelected = IsSelectedPort(symbol, portName);
            var isOccupied = IsPortOccupied(symbol.Id, portName);
            var port = new Border
            {
                Width = PortSize,
                Height = PortSize,
                CornerRadius = new CornerRadius(PortSize / 2),
                BorderThickness = new Thickness(2),
                BorderBrush = isSelected
                    ? GetThemeBrush("SignalGreenBrush", Brushes.LimeGreen)
                    : isOccupied
                    ? GetThemeBrush("TrackPlanRailBrush", Brushes.DimGray)
                    : GetThemeBrush("TrackPlanSymbolSelectedBorderBrush", Brushes.DeepSkyBlue),
                Background = isOccupied
                    ? GetThemeBrush("TrackPlanRouteHighlightBrush", Brushes.DeepSkyBlue)
                    : GetThemeBrush("PanelBackgroundBrush", Brushes.White),
                Tag = new SymbolPort(symbol, portName),
                Child = new TextBlock
                {
                    Text = portName,
                    FontSize = 9,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Foreground = GetThemeBrush("TextPrimaryBrush", Brushes.Black)
                }
            };

            port.PointerPressed += Port_OnPointerPressed;
            Canvas.SetLeft(port, point.X - PortSize / 2);
            Canvas.SetTop(port, point.Y - PortSize / 2);
            TrackCanvas.Children.Add(port);
        }
    }

    private bool IsSelectedPort(DrawnTrackSymbol symbol, string portName)
    {
        return _connectionStartPort is not null &&
               _connectionStartPort.Symbol.Id == symbol.Id &&
               string.Equals(_connectionStartPort.Name, portName, StringComparison.OrdinalIgnoreCase);
    }

    private Control CreateOperationSymbolControl(DrawnTrackSymbol symbol)
    {
        var control = symbol.Kind switch
        {
            TrackSymbolKind.Signal => CreateOperationSignal(symbol),
            TrackSymbolKind.TrackBlock => CreateOperationBlock(symbol),
            TrackSymbolKind.Switch or TrackSymbolKind.DoubleSlipSwitch => CreateOperationSwitch(symbol),
            _ => CreateOperationAccessory(symbol)
        };

        control.Tag = symbol;
        control.PointerPressed += Symbol_OnPointerPressed;
        control.ContextMenu = CreateSymbolContextMenu(symbol);
        Canvas.SetLeft(control, symbol.X - control.Width / 2);
        Canvas.SetTop(control, symbol.Y - control.Height / 2);
        return control;
    }

    private Control CreateOperationSignal(DrawnTrackSymbol symbol)
    {
        var isSelected = symbol == _selectedOperationSymbol;
        var signalBrush = _greenSignalIds.Contains(symbol.Id)
            ? IltisGreenBrush
            : IltisRedBrush;
        var borderBrush = isSelected
            ? IltisSelectionBrush
            : Brushes.Transparent;

        var canvas = new Canvas
        {
            Width = 70,
            Height = 48,
            Background = Brushes.Transparent
        };

        canvas.Children.Add(new Line
        {
            StartPoint = new Point(18, 10),
            EndPoint = new Point(18, 38),
            Stroke = IltisTextBrush,
            StrokeThickness = 3
        });

        canvas.Children.Add(new Ellipse
        {
            Width = 18,
            Height = 18,
            Fill = signalBrush,
            Stroke = IltisTextBrush,
            StrokeThickness = 2
        });
        Canvas.SetLeft(canvas.Children[^1], 9);
        Canvas.SetTop(canvas.Children[^1], 5);

        canvas.Children.Add(new TextBlock
        {
            Text = GetSignalArrow(symbol.SignalDirection),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = IltisTextBrush
        });
        Canvas.SetLeft(canvas.Children[^1], 36);
        Canvas.SetTop(canvas.Children[^1], 4);

        canvas.Children.Add(new TextBlock
        {
            Text = symbol.Name,
            FontSize = 11,
            Foreground = IltisTextBrush
        });
        Canvas.SetLeft(canvas.Children[^1], 0);
        Canvas.SetTop(canvas.Children[^1], 31);

        return new Border
        {
            Width = 78,
            Height = 54,
            BorderThickness = new Thickness(2),
            BorderBrush = borderBrush,
            Background = IltisPanelBrush,
            Child = canvas
        };
    }

    private Control CreateOperationBlock(DrawnTrackSymbol symbol)
    {
        var isOccupied = _occupiedSymbolIds.Contains(symbol.Id);
        var isSelected = symbol == _selectedOperationSymbol;

        return new Border
        {
            Width = 104,
            Height = 38,
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(isSelected ? 3 : 2),
            BorderBrush = isSelected
                ? IltisSelectionBrush
                : IltisRailBrush,
            Background = isOccupied
                ? IltisRedBrush
                : IltisPanelBrush,
            Child = new TextBlock
            {
                Text = symbol.Name,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = isOccupied ? IltisDarkTextBrush : IltisTextBrush
            }
        };
    }

    private Control CreateOperationSwitch(DrawnTrackSymbol symbol)
    {
        var isSelected = symbol == _selectedOperationSymbol;
        var activeBrush = IltisGreenBrush;
        var railBrush = IltisRailBrush;
        var inactiveBrush = IltisInactiveRailBrush;

        var canvas = new Canvas
        {
            Width = 96,
            Height = 44,
            Background = Brushes.Transparent
        };

        if (symbol.Kind is TrackSymbolKind.DoubleSlipSwitch)
        {
            // DKW-Darstellung:
            // - Left/Right sind die beiden diagonalen Wege.
            // - Straight/Diverging sind die oberen bzw. unteren Slip-Wege.
            // Eine mittlere Gerade wird absichtlich nicht gezeichnet, weil sie bei
            // dieser DKW-Symbolik keinen realen Fahrweg darstellt.
            AddOperationSwitchLine(canvas, new Point(10, 8), new Point(86, 36), inactiveBrush, 5);
            AddOperationSwitchLine(canvas, new Point(10, 36), new Point(86, 8), inactiveBrush, 5);
            AddOperationSwitchPolyline(canvas, [new Point(10, 8), new Point(36, 14), new Point(60, 14), new Point(86, 8)], inactiveBrush, 4);
            AddOperationSwitchPolyline(canvas, [new Point(10, 36), new Point(36, 30), new Point(60, 30), new Point(86, 36)], inactiveBrush, 4);

            switch (symbol.CurrentSwitchPosition)
            {
                case SwitchPosition.Straight:
                    AddOperationSwitchPolyline(canvas, [new Point(10, 8), new Point(36, 14), new Point(60, 14), new Point(86, 8)], activeBrush, 5);
                    break;
                case SwitchPosition.Diverging:
                    AddOperationSwitchPolyline(canvas, [new Point(10, 36), new Point(36, 30), new Point(60, 30), new Point(86, 36)], activeBrush, 5);
                    break;
                case SwitchPosition.Left:
                    AddOperationSwitchLine(canvas, new Point(10, 36), new Point(86, 8), activeBrush, 5);
                    break;
                case SwitchPosition.Right:
                    AddOperationSwitchLine(canvas, new Point(10, 8), new Point(86, 36), activeBrush, 5);
                    break;
                default:
                    AddOperationSwitchLine(canvas, new Point(10, 36), new Point(86, 8), activeBrush, 5);
                    break;
            }
        }
        else
        {
            var geometry = GetSwitchGeometry(symbol);
            AddOperationSwitchLine(canvas, geometry.StraightStart, geometry.StraightEnd, railBrush, 6);
            AddOperationSwitchLine(canvas, geometry.DivergingStart, geometry.DivergingEnd, inactiveBrush, 4);

            if (symbol.CurrentSwitchPosition is SwitchPosition.Diverging or SwitchPosition.Left or SwitchPosition.Right)
            {
                AddOperationSwitchLine(canvas, geometry.DivergingStart, geometry.DivergingEnd, activeBrush, 5);
            }
            else
            {
                AddOperationSwitchLine(canvas, geometry.StraightStart, geometry.StraightEnd, activeBrush, 5);
            }
        }

        canvas.Children.Add(new TextBlock
        {
            Text = GetIltisSwitchPositionText(symbol.CurrentSwitchPosition),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = IltisTextBrush
        });
        Canvas.SetLeft(canvas.Children[^1], 35);
        Canvas.SetTop(canvas.Children[^1], 27);

        return new Border
        {
            Width = 102,
            Height = 52,
            BorderThickness = new Thickness(isSelected ? 2 : 0),
            BorderBrush = IltisSelectionBrush,
            Background = IltisPanelBrush,
            Child = canvas
        };
    }

    private static void AddOperationSwitchLine(
        Canvas canvas,
        Point start,
        Point end,
        IBrush brush,
        double thickness)
    {
        canvas.Children.Add(new Line
        {
            StartPoint = start,
            EndPoint = end,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeLineCap = PenLineCap.Round
        });
    }

    private static void AddOperationSwitchPolyline(
        Canvas canvas,
        IEnumerable<Point> points,
        IBrush brush,
        double thickness)
    {
        canvas.Children.Add(new Polyline
        {
            Points = [.. points],
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeLineCap = PenLineCap.Round
        });
    }

    private static void ApplyIltisConnectionStyle(
        Line line,
        bool isReleased,
        bool isActive,
        bool isHighlighted)
    {
        line.Stroke = isActive
            ? IltisGreenBrush
            : isHighlighted
            ? IltisYellowBrush
            : isReleased
            ? IltisInactiveRailBrush
            : IltisRailBrush;

        line.StrokeThickness = isActive || isHighlighted ? 7 : 5;
    }

    private Control CreateOperationAccessory(DrawnTrackSymbol symbol)
    {
        var isSelected = symbol == _selectedOperationSymbol;

        return new Border
        {
            Width = 82,
            Height = 34,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            BorderBrush = isSelected
                ? IltisSelectionBrush
                : IltisRailBrush,
            Background = IltisPanelBrush,
            Child = new TextBlock
            {
                Text = GetSymbolIcon(symbol),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = IltisTextBrush
            }
        };
    }

    private ContextMenu CreateSymbolContextMenu(DrawnTrackSymbol symbol)
    {
        var settingsItem = new MenuItem { Header = "Einstellungen" };
        settingsItem.Click += async (_, _) => await OpenElementSettingsAsync(symbol);

        var renameItem = new MenuItem { Header = "Umbenennen" };
        renameItem.Click += async (_, _) => await RenameSymbolAsync(symbol);

        var connectItem = new MenuItem { Header = "Verbinden" };
        connectItem.Click += (_, _) =>
        {
            _activeTool = TrackEditorTool.Connect;
            _connectionStart = null;
            _connectionStartPort = null;
            TrackPlanStatus.Text = "Verbinden aktiv. Ersten Port anklicken.";
            RenderTrackPlan();
        };

        var removeItem = new MenuItem { Header = "Entfernen" };
        removeItem.Click += (_, _) => RemoveSymbol(symbol);

        var menu = new ContextMenu
        {
            Items =
            {
                settingsItem
            }
        };

        if (_isOperationMode)
        {
            return menu;
        }

        menu.Items.Add(renameItem);
        menu.Items.Add(connectItem);
        menu.Items.Add(new Separator());

        if (symbol.Kind is TrackSymbolKind.Signal)
        {
            var directionMenu = new MenuItem { Header = "Signalrichtung" };
            foreach (var direction in Enum.GetValues<SignalDirection>())
            {
                var directionItem = new MenuItem { Header = GetSignalDirectionText(direction) };
                directionItem.Click += (_, _) =>
                {
                    symbol.SignalDirection = direction;
                    TrackPlanStatus.Text = $"{symbol.Name}: Richtung {GetSignalDirectionText(direction)}.";
                    SaveTrackPlanAndClearRoutes();
                    RenderTrackPlan();
                };
                directionMenu.Items.Add(directionItem);
            }

            menu.Items.Add(directionMenu);
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(removeItem);
        return menu;
    }

    private void Symbol_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: DrawnTrackSymbol symbol })
        {
            return;
        }

        if (e.GetCurrentPoint(TrackCanvas).Properties.IsRightButtonPressed)
        {
            return;
        }

        e.Handled = true;

        if (_isOperationMode)
        {
            if (symbol.Kind is TrackSymbolKind.Signal)
            {
                SelectOperationSignal(symbol);
                return;
            }

            _selectedOperationSymbol = symbol;
            TrackPlanStatus.Text = symbol.Kind is TrackSymbolKind.TrackBlock
                ? $"{symbol.Name} ausgewaehlt. Block kann belegt oder freigegeben werden."
                : $"{symbol.Name} ausgewaehlt.";
            RenderTrackPlan();
            return;
        }

        if (_activeTool is not TrackEditorTool.Connect)
        {
            var pointerPosition = e.GetPosition(TrackCanvas);
            _draggedSymbol = symbol;
            _dragOffset = new Point(pointerPosition.X - symbol.X, pointerPosition.Y - symbol.Y);
            _isDragging = true;
            TrackCanvas.PointerMoved += TrackCanvas_OnPointerMoved;
            TrackCanvas.PointerReleased += TrackCanvas_OnPointerReleased;
            return;
        }

        TrackPlanStatus.Text = _connectionStartPort is null
            ? "Zum Verbinden den ersten Port anklicken."
            : $"{_connectionStartPort.Symbol.Name}:{_connectionStartPort.Name} gewaehlt. Ziel-Port anklicken.";
    }

    private void SelectOperationSignal(DrawnTrackSymbol signal)
    {
        _selectedOperationSymbol = signal;

        if (_operationStartSignal is null || _operationStartSignal.Id == signal.Id)
        {
            _operationStartSignal = signal;
            _selectedOperationRoute = null;
            _highlightedConnectionKeys.Clear();
            TrackPlanStatus.Text = $"{signal.Name} als Startsignal gewaehlt. Zielsignal anklicken.";
            RenderTrackPlan();
            return;
        }

        var route = FindOperationRoute(_operationStartSignal.Id, signal.Id);
        if (route is null)
        {
            TrackPlanStatus.Text = $"Keine Fahrstrasse von {_operationStartSignal.Name} nach {signal.Name} gefunden.";
            _operationStartSignal = null;
            _selectedOperationRoute = null;
            _highlightedConnectionKeys.Clear();
            RenderTrackPlan();
            return;
        }

        _selectedOperationRoute = route;
        _operationStartSignal = null;
        HighlightRoute(route);
        SetSelectedOperationRoute();
    }

    private RouteResult? FindOperationRoute(string startSignalId, string targetSignalId)
    {
        var existingRoute = _visibleRoutes.FirstOrDefault(route =>
            route.StartSignal.Id == startSignalId &&
            route.TargetSignal.Id == targetSignalId);

        if (existingRoute is not null)
        {
            return existingRoute;
        }

        var graph = _trackPlanEditor.ToGraph();
        var result = _routeBuilder.FindRoute(graph, startSignalId, targetSignalId);
        if (!result.IsSuccess || result.Route is null)
        {
            return null;
        }

        _visibleRoutes.Add(result.Route);
        RefreshRouteLists();
        SaveRoutes();
        return result.Route;
    }

    private void Port_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: SymbolPort port } || _isOperationMode)
        {
            return;
        }

        if (!e.GetCurrentPoint(TrackCanvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;

        if (_connectionStartPort is null)
        {
            if (IsPortOccupied(port.Symbol.Id, port.Name))
            {
                TrackPlanStatus.Text = $"{port.Symbol.Name}:{port.Name} ist bereits verbunden.";
                return;
            }

            _activeTool = TrackEditorTool.Connect;
            _connectionStart = port.Symbol;
            _connectionStartPort = port;
            TrackPlanStatus.Text = $"{port.Symbol.Name}:{port.Name} gewaehlt. Ziel-Port anklicken.";
            RenderTrackPlan();
            return;
        }

        if (IsPortOccupied(port.Symbol.Id, port.Name))
        {
            TrackPlanStatus.Text = $"{port.Symbol.Name}:{port.Name} ist bereits verbunden.";
            return;
        }

        if (_connectionStartPort.Symbol.Id == port.Symbol.Id &&
            string.Equals(_connectionStartPort.Name, port.Name, StringComparison.OrdinalIgnoreCase))
        {
            _connectionStart = null;
            _connectionStartPort = null;
            TrackPlanStatus.Text = "Port-Auswahl aufgehoben.";
            RenderTrackPlan();
            return;
        }

        _trackPlanEditor.Connect(
            _connectionStartPort.Symbol.Id,
            _connectionStartPort.Name,
            port.Symbol.Id,
            port.Name);

        TrackPlanStatus.Text = $"{_connectionStartPort.Symbol.Name}:{_connectionStartPort.Name} mit {port.Symbol.Name}:{port.Name} verbunden.";
        _connectionStart = null;
        _connectionStartPort = null;
        SaveTrackPlanAndClearRoutes();
        RenderTrackPlan();
    }

    private ContextMenu CreateConnectionContextMenu(DrawnTrackConnection connection)
    {
        var removeItem = new MenuItem { Header = "Verbindung entfernen" };
        removeItem.Click += (_, _) => RemoveConnection(connection);

        return new ContextMenu
        {
            Items =
            {
                removeItem
            }
        };
    }

    private void Connection_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Line { Tag: DrawnTrackConnection connection } || _isOperationMode)
        {
            return;
        }

        if (!e.GetCurrentPoint(TrackCanvas).Properties.IsRightButtonPressed)
        {
            return;
        }

        TrackPlanStatus.Text = FormatConnection(connection);
    }

    private void RemoveConnection(DrawnTrackConnection connection)
    {
        if (!_trackPlanEditor.Document.Connections.Remove(connection))
        {
            return;
        }

        _highlightedConnectionKeys.Clear();
        _activeRouteConnectionKeys.Clear();
        _releasedRouteConnectionKeys.Clear();
        _activeRoutes.Clear();
        _greenSignalIds.Clear();
        _lockedSymbolIds.Clear();
        _visibleRoutes.Clear();
        RouteList.Items.Clear();
        OperationRouteList.Items.Clear();
        UpdateActiveRouteText();
        TrackPlanStatus.Text = $"{FormatConnection(connection)} entfernt.";
        SaveTrackPlanAndClearRoutes();
        RenderTrackPlan();
    }

    private bool IsPortOccupied(string symbolId, string portName)
    {
        return _trackPlanEditor.Document.Connections.Any(connection =>
            (connection.FromSymbolId == symbolId &&
             string.Equals(connection.FromPort, portName, StringComparison.OrdinalIgnoreCase)) ||
            (connection.ToSymbolId == symbolId &&
             string.Equals(connection.ToPort, portName, StringComparison.OrdinalIgnoreCase)));
    }

    private string FormatConnection(DrawnTrackConnection connection)
    {
        var from = FindSymbol(connection.FromSymbolId)?.Name ?? connection.FromSymbolId;
        var to = FindSymbol(connection.ToSymbolId)?.Name ?? connection.ToSymbolId;
        return $"{from}:{connection.FromPort} -> {to}:{connection.ToPort}";
    }

    private async System.Threading.Tasks.Task OpenElementSettingsAsync(DrawnTrackSymbol symbol)
    {
        var originalName = symbol.Name;
        var originalSignalDirection = symbol.SignalDirection;
        var originalProperties = symbol.Properties.ToDictionary(
            static item => item.Key,
            static item => item.Value);

        var dialog = new Window
        {
            Title = $"Einstellungen - {symbol.Name}",
            Width = 440,
            Height = 560,
            MinWidth = 420,
            MinHeight = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = GetThemeBrush("PanelBackgroundBrush", Brushes.Black)
        };

        var nameBox = new TextBox
        {
            Text = symbol.Name,
            MinWidth = 280
        };

        var content = new StackPanel
        {
            Spacing = 12
        };

        content.Children.Add(new TextBlock
        {
            Text = $"{GetSymbolIcon(symbol.Kind)} {symbol.Kind}",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold
        });

        content.Children.Add(new TextBlock
        {
            Text = "Name",
            Classes = { "secondary" }
        });
        content.Children.Add(nameBox);

        AddElementSpecificSettings(content, symbol);
        AddDemoSettings(content, symbol);
        AddDomino67Settings(content, symbol);

        var saveButton = new Button
        {
            Content = "Speichern",
            Classes = { "primary" },
            MinWidth = 100
        };

        var cancelButton = new Button
        {
            Content = "Abbrechen",
            MinWidth = 100
        };

        saveButton.Click += (_, _) =>
        {
            dialog.Close(true);
        };

        cancelButton.Click += (_, _) =>
        {
            dialog.Close(false);
        };

        var buttonBar = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, saveButton }
        };

        dialog.Content = new Border
        {
            Padding = new Thickness(18),
            Child = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto)
                },
                Children =
                {
                    new ScrollViewer
                    {
                        Content = content
                    },
                    buttonBar
                }
            }
        };
        Grid.SetRow(buttonBar, 1);

        var owner = this.GetVisualRoot() as Window;
        var result = owner is null
            ? await dialog.ShowDialog<bool>(new Window())
            : await dialog.ShowDialog<bool>(owner);

        if (!result)
        {
            // Die Controls schreiben ihre Werte bereits waehrend der Dialog offen ist
            // in das Symbol. Bei Abbrechen wird deshalb der vorherige Zustand komplett
            // wiederhergestellt.
            symbol.Name = originalName;
            symbol.SignalDirection = originalSignalDirection;
            symbol.Properties.Clear();
            foreach (var item in originalProperties)
            {
                symbol.Properties[item.Key] = item.Value;
            }

            RenderTrackPlan();
            return;
        }

        var newName = nameBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(newName))
        {
            symbol.Name = newName;
        }

        TrackPlanStatus.Text = $"{symbol.Name}: Einstellungen gespeichert.";
        SaveTrackPlan();
        RenderTrackPlan();
    }

    private void AddElementSpecificSettings(StackPanel content, DrawnTrackSymbol symbol)
    {
        if (symbol.Kind is TrackSymbolKind.Signal)
        {
            content.Children.Add(CreateSectionTitle("Signal"));
            var signalDirectionBox = new ComboBox
            {
                MinWidth = 240
            };

            foreach (var direction in Enum.GetValues<SignalDirection>())
            {
                signalDirectionBox.Items.Add(direction);
            }

            signalDirectionBox.SelectedItem = symbol.SignalDirection;
            signalDirectionBox.SelectionChanged += (_, _) =>
            {
                if (signalDirectionBox.SelectedItem is SignalDirection direction)
                {
                    symbol.SignalDirection = direction;
                }
            };

            content.Children.Add(CreateLabeledControl("Signalrichtung", signalDirectionBox));
        }

        if (symbol.Kind is TrackSymbolKind.Switch)
        {
            content.Children.Add(CreateSectionTitle("Weiche"));
            var orientationBox = new ComboBox
            {
                MinWidth = 240
            };

            var orientations = new[]
            {
                SwitchOrientation.LeftRightUp,
                SwitchOrientation.LeftRightDown,
                SwitchOrientation.RightLeftUp,
                SwitchOrientation.RightLeftDown
            };

            foreach (var orientation in orientations)
            {
                orientationBox.Items.Add(orientation);
            }

            orientationBox.SelectedItem = GetSwitchOrientation(symbol);
            orientationBox.SelectionChanged += (_, _) =>
            {
                if (orientationBox.SelectedItem is string orientation)
                {
                    symbol.Properties[SwitchOrientationProperty] = orientation;
                }
            };

            content.Children.Add(CreateLabeledControl("Orientierung", orientationBox));
        }
    }

    private void AddDemoSettings(StackPanel content, DrawnTrackSymbol symbol)
    {
        content.Children.Add(CreateSectionTitle("Demo-Stellwerkslogik"));
        content.Children.Add(CreatePropertyCheckBox(symbol, new DemoSetting("Demo.Blocked", "Element sperren", "true")));

        // Jede Checkbox schreibt direkt in symbol.Properties. Die Elementlogiken lesen
        // diese Properties beim naechsten Stellversuch aus.
        foreach (var setting in GetDemoSettings(symbol.Kind))
        {
            content.Children.Add(CreatePropertyCheckBox(symbol, setting));
        }

        if (symbol.Kind is TrackSymbolKind.Switch or TrackSymbolKind.DoubleSlipSwitch)
        {
            var lockedPositionBox = new ComboBox
            {
                MinWidth = 240
            };

            lockedPositionBox.Items.Add("Keine");
            foreach (var position in Enum.GetValues<SwitchPosition>())
            {
                lockedPositionBox.Items.Add(position.ToString());
            }

            lockedPositionBox.SelectedItem = symbol.Properties.TryGetValue("Demo.LockedPosition", out var positionValue)
                ? positionValue
                : "Keine";
            lockedPositionBox.SelectionChanged += (_, _) =>
            {
                if (lockedPositionBox.SelectedItem is not string selected ||
                    string.Equals(selected, "Keine", StringComparison.OrdinalIgnoreCase))
                {
                    symbol.Properties.Remove("Demo.LockedPosition");
                    return;
                }

                symbol.Properties["Demo.LockedPosition"] = selected;
            };

            content.Children.Add(CreateLabeledControl("Demo-Lage verschlossen", lockedPositionBox));
        }
    }

    private void AddDomino67Settings(StackPanel content, DrawnTrackSymbol symbol)
    {
        content.Children.Add(CreateSectionTitle("Domino 67"));
        content.Children.Add(CreatePropertyCheckBox(symbol, new DemoSetting(Domino67PropertyNames.Blocked, "Element im Domino 67 sperren", "true")));

        if (symbol.Kind is TrackSymbolKind.Signal)
        {
            content.Children.Add(CreatePropertyCheckBox(symbol, new DemoSetting(Domino67PropertyNames.HoldRed, "Startsignal auf Halt halten", "true")));
            content.Children.Add(CreatePropertyTextBox(symbol, Domino67PropertyNames.FlankProtectionSymbols, "Flankenschutz-Symbole", "Symbol-IDs mit Komma trennen"));
            content.Children.Add(CreatePropertyTextBox(symbol, Domino67PropertyNames.OverlapSymbols, "Durchrutschweg-Symbole", "Symbol-IDs mit Komma trennen"));
        }

        if (symbol.Kind is TrackSymbolKind.TrackBlock)
        {
            content.Children.Add(CreatePropertyTextBox(symbol, Domino67PropertyNames.OverlapSymbols, "Durchrutschweg-Symbole", "Symbol-IDs mit Komma trennen"));
        }

        if (symbol.Kind is TrackSymbolKind.Switch or TrackSymbolKind.DoubleSlipSwitch)
        {
            content.Children.Add(CreatePropertyCheckBox(symbol, new DemoSetting(Domino67PropertyNames.LocalControl, "Ortsbetrieb aktiv", "true")));
            content.Children.Add(CreatePropertyCheckBox(symbol, new DemoSetting(Domino67PropertyNames.Locked, "Weiche verschlossen", "true")));
            content.Children.Add(CreatePropertyCheckBox(symbol, new DemoSetting(Domino67PropertyNames.FlankProtectionEnabled, "Als Schutzweiche verwenden", "true")));

            var requiredPositionBox = new ComboBox
            {
                MinWidth = 240
            };

            requiredPositionBox.Items.Add("Keine");
            foreach (var position in Enum.GetValues<SwitchPosition>())
            {
                requiredPositionBox.Items.Add(position.ToString());
            }

            requiredPositionBox.SelectedItem = symbol.Properties.TryGetValue(Domino67PropertyNames.RequiredPosition, out var requiredPosition)
                ? requiredPosition
                : "Keine";
            requiredPositionBox.SelectionChanged += (_, _) =>
            {
                if (requiredPositionBox.SelectedItem is not string selected ||
                    string.Equals(selected, "Keine", StringComparison.OrdinalIgnoreCase))
                {
                    symbol.Properties.Remove(Domino67PropertyNames.RequiredPosition);
                    return;
                }

                symbol.Properties[Domino67PropertyNames.RequiredPosition] = selected;
            };

            content.Children.Add(CreateLabeledControl("Domino-67-Pflichtlage", requiredPositionBox));
        }

        if (symbol.Kind is TrackSymbolKind.LevelCrossing)
        {
            content.Children.Add(CreatePropertyCheckBox(symbol, new DemoSetting(Domino67PropertyNames.LevelCrossingClosed, "Bahnuebergang geschlossen", "true")));
            content.Children.Add(CreatePropertyCheckBox(symbol, new DemoSetting(Domino67PropertyNames.LevelCrossingAutoClose, "Bahnuebergang automatisch schliessen", "true")));
        }
    }

    private CheckBox CreatePropertyCheckBox(DrawnTrackSymbol symbol, DemoSetting setting)
    {
        var checkBox = new CheckBox
        {
            Content = setting.Label,
            IsChecked = symbol.Properties.TryGetValue(setting.Key, out var value) &&
                        string.Equals(value, setting.EnabledValue, StringComparison.OrdinalIgnoreCase)
        };

        checkBox.IsCheckedChanged += (_, _) =>
        {
            if (checkBox.IsChecked is true)
            {
                symbol.Properties[setting.Key] = setting.EnabledValue;
                return;
            }

            symbol.Properties.Remove(setting.Key);
        };

        return checkBox;
    }

    private Control CreatePropertyTextBox(
        DrawnTrackSymbol symbol,
        string propertyName,
        string label,
        string watermark)
    {
        var textBox = new TextBox
        {
            Text = symbol.Properties.TryGetValue(propertyName, out var value) ? value : string.Empty,
            Watermark = watermark,
            MinWidth = 280
        };

        textBox.TextChanged += (_, _) =>
        {
            var text = textBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                symbol.Properties.Remove(propertyName);
                return;
            }

            symbol.Properties[propertyName] = text;
        };

        return CreateLabeledControl(label, textBox);
    }

    private static IReadOnlyList<DemoSetting> GetDemoSettings(TrackSymbolKind kind)
    {
        return kind switch
        {
            TrackSymbolKind.Track => [new DemoSetting("Demo.Maintenance", "Gleis im Unterhalt", "true")],
            TrackSymbolKind.TrackBlock => [new DemoSetting("Demo.ReserveOnly", "Nur Reservemanöver", "true")],
            TrackSymbolKind.Signal => [new DemoSetting("Demo.HoldRed", "Signal auf Halt halten", "true")],
            TrackSymbolKind.Crossing => [new DemoSetting("Demo.ConflictingCrossing", "Kreuzungskonflikt aktiv", "true")],
            TrackSymbolKind.Sensor => [new DemoSetting("Demo.SensorClear", "Sensor meldet nicht frei", "false")],
            TrackSymbolKind.Platform => [new DemoSetting("Demo.PassengerStopOnly", "Nur haltende Zuege", "true")],
            TrackSymbolKind.LevelCrossing => [new DemoSetting("Demo.Closed", "Bahnuebergang nicht geschlossen", "false")],
            TrackSymbolKind.TunnelPortal => [new DemoSetting("Demo.TunnelClear", "Tunnel nicht frei", "false")],
            TrackSymbolKind.Bridge => [new DemoSetting("Demo.BridgeReleased", "Bruecke nicht freigegeben", "false")],
            TrackSymbolKind.Uncoupler => [new DemoSetting("Demo.Lowered", "Entkuppler nicht abgesenkt", "false")],
            TrackSymbolKind.Depot => [new DemoSetting("Demo.DepotExitReleased", "Depot-Ausfahrt nicht freigegeben", "false")],
            TrackSymbolKind.Turntable => [new DemoSetting("Demo.Aligned", "Drehscheibe nicht ausgerichtet", "false")],
            TrackSymbolKind.TextLabel => [new DemoSetting("Demo.OperationalLabel", "Betriebliche Demo-Sperre", "true")],
            _ => []
        };
    }

    private static TextBlock CreateSectionTitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 8, 0, 0)
        };
    }

    private static Control CreateLabeledControl(string label, Control control)
    {
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Classes = { "secondary" }
                },
                control
            }
        };
    }

    private async System.Threading.Tasks.Task RenameSymbolAsync(DrawnTrackSymbol symbol)
    {
        var dialog = new Window
        {
            Title = "Element umbenennen",
            Width = 360,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White
        };

        var nameBox = new TextBox
        {
            Text = symbol.Name,
            MinWidth = 260
        };

        var saveButton = new Button
        {
            Content = "Speichern",
            Classes = { "primary" },
            MinWidth = 100
        };

        var cancelButton = new Button
        {
            Content = "Abbrechen",
            MinWidth = 100
        };

        saveButton.Click += (_, _) =>
        {
            dialog.Close(nameBox.Text?.Trim());
        };

        cancelButton.Click += (_, _) =>
        {
            dialog.Close(null);
        };

        dialog.Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Name", Foreground = Brushes.Black },
                    nameBox,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, saveButton }
                    }
                }
            }
        };

        var owner = this.GetVisualRoot() as Window;
        var result = owner is null
            ? await dialog.ShowDialog<string?>(new Window())
            : await dialog.ShowDialog<string?>(owner);

        if (string.IsNullOrWhiteSpace(result))
        {
            return;
        }

        symbol.Name = result;
        TrackPlanStatus.Text = $"Element in '{symbol.Name}' umbenannt.";
        SaveTrackPlan();
        RenderTrackPlan();
    }

    private void RemoveSymbol(DrawnTrackSymbol symbol)
    {
        _trackPlanEditor.Document.Connections.RemoveAll(connection =>
            connection.FromSymbolId == symbol.Id || connection.ToSymbolId == symbol.Id);

        _trackPlanEditor.Document.Symbols.Remove(symbol);

        if (_connectionStart == symbol)
        {
            _connectionStart = null;
        }

        if (_connectionStartPort?.Symbol == symbol)
        {
            _connectionStartPort = null;
        }

        _highlightedConnectionKeys.Clear();
        _activeRouteConnectionKeys.Clear();
        _releasedRouteConnectionKeys.Clear();
        _activeRoutes.Clear();
        _greenSignalIds.Clear();
        _lockedSymbolIds.Clear();
        _visibleRoutes.Clear();
        RouteList.Items.Clear();
        OperationRouteList.Items.Clear();
        UpdateActiveRouteText();
        TrackPlanStatus.Text = $"{symbol.Name} entfernt.";
        SaveTrackPlanAndClearRoutes();
        RenderTrackPlan();
    }

    private void TrackCanvas_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isOperationMode || !_isDragging || _draggedSymbol is null)
        {
            return;
        }

        var position = e.GetPosition(TrackCanvas);
        _draggedSymbol.X = Snap(position.X - _dragOffset.X);
        _draggedSymbol.Y = Snap(position.Y - _dragOffset.Y);
        _highlightedConnectionKeys.Clear();
        RenderTrackPlan();
    }

    private void TrackCanvas_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedSymbol is not null)
        {
            SaveTrackPlanAndClearRoutes();
        }

        _isDragging = false;
        _draggedSymbol = null;
        TrackCanvas.PointerMoved -= TrackCanvas_OnPointerMoved;
        TrackCanvas.PointerReleased -= TrackCanvas_OnPointerReleased;
    }

    private void HighlightRoute(RouteResult route)
    {
        _highlightedConnectionKeys.Clear();
        foreach (var connection in route.Connections)
        {
            _highlightedConnectionKeys.Add(GetConnectionKey(connection.FromSymbolId, connection.ToSymbolId));
            _highlightedConnectionKeys.Add(GetConnectionKey(connection.ToSymbolId, connection.FromSymbolId));
        }

        var switches = route.SwitchCommands.Count == 0
            ? "keine Weichen"
            : string.Join(", ", route.SwitchCommands.Select(command => $"{command.SwitchName} -> {command.Position}"));

        TrackPlanStatus.Text = $"{route.StartSignal.Name} -> {route.TargetSignal.Name}; {switches}.";
        RenderTrackPlan();
    }

    private static string FormatSwitchCommands(RouteResult route)
    {
        return FormatSwitchCommands(route.SwitchCommands);
    }

    private static string FormatSwitchCommands(IReadOnlyList<SwitchCommand> switchCommands)
    {
        return switchCommands.Count == 0
            ? "keine Weichen"
            : string.Join(", ", switchCommands.Select(command => $"{command.SwitchName} -> {command.Position}"));
    }

    private void ReleaseActiveRouteElement(DrawnTrackSymbol symbol)
    {
        var affectedRoutes = _activeRoutes
            .Where(route => route.Symbols.Any(routeSymbol => routeSymbol.Id == symbol.Id))
            .ToList();

        if (affectedRoutes.Count == 0)
        {
            return;
        }

        var releasedKeys = affectedRoutes
            .SelectMany(route => route.Connections)
            .SelectMany(connection => new[]
            {
                GetConnectionKey(connection.FromSymbolId, connection.ToSymbolId),
                GetConnectionKey(connection.ToSymbolId, connection.FromSymbolId)
            })
            .ToList();

        foreach (var key in releasedKeys)
        {
            _activeRouteConnectionKeys.Remove(key);
            _releasedRouteConnectionKeys.Add(key);
        }

        foreach (var route in affectedRoutes)
        {
            _activeRoutes.Remove(route);
            _greenSignalIds.Remove(route.StartSignal.Id);
        }

        RebuildLockedSymbols();
        UpdateActiveRouteText();

        if (_activeRoutes.Count == 0)
        {
            _releasedRouteConnectionKeys.Clear();
        }
    }

    private void UpdateActiveRouteText()
    {
        ActiveRouteText.Text = _activeRoutes.Count == 0
            ? "Keine Fahrstrasse aktiv."
            : $"{_activeRoutes.Count} Fahrstrassen aktiv.";
    }

    private void RebuildLockedSymbols()
    {
        _lockedSymbolIds.Clear();
        foreach (var route in _activeRoutes)
        {
            foreach (var symbol in route.Symbols)
            {
                _lockedSymbolIds.Add(symbol.Id);
            }
        }
    }

    private DrawnTrackSymbol? FindSymbol(string id)
    {
        return _trackPlanEditor.Document.Symbols.FirstOrDefault(symbol => symbol.Id == id);
    }

    private static (string FromPort, string ToPort) GuessPorts(DrawnTrackSymbol from, DrawnTrackSymbol to)
    {
        var fromPort = from.Kind switch
        {
            TrackSymbolKind.Switch => GetSwitchPortForNeighbor(from, to),
            TrackSymbolKind.DoubleSlipSwitch when to.Y > from.Y + 20 => "C",
            TrackSymbolKind.DoubleSlipSwitch when to.Y < from.Y - 20 => "D",
            TrackSymbolKind.DoubleSlipSwitch when to.X > from.X => "B",
            TrackSymbolKind.DoubleSlipSwitch => "A",
            TrackSymbolKind.Signal => "out",
            _ when to.X >= from.X => "right",
            _ => "left"
        };

        var toPort = to.Kind switch
        {
            TrackSymbolKind.Switch => GetSwitchPortForNeighbor(to, from),
            TrackSymbolKind.DoubleSlipSwitch when from.X < to.X => "A",
            TrackSymbolKind.DoubleSlipSwitch when from.Y > to.Y + 20 => "C",
            TrackSymbolKind.DoubleSlipSwitch when from.Y < to.Y - 20 => "D",
            TrackSymbolKind.DoubleSlipSwitch => "B",
            TrackSymbolKind.Signal => "in",
            _ when from.X <= to.X => "left",
            _ => "right"
        };

        return (fromPort, toPort);
    }

    private static IReadOnlyList<string> GetPortNames(DrawnTrackSymbol symbol)
    {
        return symbol.Kind switch
        {
            TrackSymbolKind.Signal => ["in", "out"],
            TrackSymbolKind.Switch => ["A", "B", "C"],
            TrackSymbolKind.DoubleSlipSwitch => ["A", "B", "C", "D"],
            TrackSymbolKind.Track or TrackSymbolKind.TrackBlock => ["left", "right"],
            _ => ["left", "right"]
        };
    }

    private static Point GetPortPoint(DrawnTrackSymbol symbol, string portName)
    {
        if (symbol.Kind is TrackSymbolKind.Signal)
        {
            return GetSignalPortPoint(symbol, portName);
        }

        if (symbol.Kind is TrackSymbolKind.Switch)
        {
            return GetSwitchPortPoint(symbol, portName);
        }

        if (symbol.Kind is TrackSymbolKind.DoubleSlipSwitch)
        {
            return portName.ToUpperInvariant() switch
            {
                "A" => new Point(symbol.X - 46, symbol.Y - 18),
                "B" => new Point(symbol.X + 46, symbol.Y - 18),
                "C" => new Point(symbol.X - 46, symbol.Y + 18),
                "D" => new Point(symbol.X + 46, symbol.Y + 18),
                _ => new Point(symbol.X, symbol.Y)
            };
        }

        return portName.ToLowerInvariant() switch
        {
            "left" => new Point(symbol.X - 46, symbol.Y),
            "right" => new Point(symbol.X + 46, symbol.Y),
            _ => new Point(symbol.X, symbol.Y)
        };
    }

    private static Point GetSignalPortPoint(DrawnTrackSymbol symbol, string portName)
    {
        var isOut = string.Equals(portName, "out", StringComparison.OrdinalIgnoreCase);

        return symbol.SignalDirection switch
        {
            SignalDirection.RightToLeft => isOut
                ? new Point(symbol.X - 38, symbol.Y)
                : new Point(symbol.X + 38, symbol.Y),
            SignalDirection.TopToBottom => isOut
                ? new Point(symbol.X, symbol.Y + 28)
                : new Point(symbol.X, symbol.Y - 28),
            SignalDirection.BottomToTop => isOut
                ? new Point(symbol.X, symbol.Y - 28)
                : new Point(symbol.X, symbol.Y + 28),
            _ => isOut
                ? new Point(symbol.X + 38, symbol.Y)
                : new Point(symbol.X - 38, symbol.Y)
        };
    }

    private static Point GetSwitchPortPoint(DrawnTrackSymbol symbol, string portName)
    {
        return GetSwitchOrientation(symbol) switch
        {
            SwitchOrientation.LeftRightDown => portName.ToUpperInvariant() switch
            {
                "A" => new Point(symbol.X - 46, symbol.Y),
                "B" => new Point(symbol.X + 46, symbol.Y),
                "C" => new Point(symbol.X + 38, symbol.Y + 28),
                _ => new Point(symbol.X, symbol.Y)
            },
            SwitchOrientation.RightLeftUp => portName.ToUpperInvariant() switch
            {
                "A" => new Point(symbol.X + 46, symbol.Y),
                "B" => new Point(symbol.X - 46, symbol.Y),
                "C" => new Point(symbol.X - 38, symbol.Y - 28),
                _ => new Point(symbol.X, symbol.Y)
            },
            SwitchOrientation.RightLeftDown => portName.ToUpperInvariant() switch
            {
                "A" => new Point(symbol.X + 46, symbol.Y),
                "B" => new Point(symbol.X - 46, symbol.Y),
                "C" => new Point(symbol.X - 38, symbol.Y + 28),
                _ => new Point(symbol.X, symbol.Y)
            },
            _ => portName.ToUpperInvariant() switch
            {
                "A" => new Point(symbol.X - 46, symbol.Y),
                "B" => new Point(symbol.X + 46, symbol.Y),
                "C" => new Point(symbol.X + 38, symbol.Y - 28),
                _ => new Point(symbol.X, symbol.Y)
            }
        };
    }

    private static int Snap(double value)
    {
        const int grid = 20;
        return (int)Math.Round(value / grid) * grid;
    }

    private static string GetConnectionKey(string fromId, string toId)
    {
        return $"{fromId}->{toId}";
    }

    private IBrush GetSymbolBackground(TrackSymbolKind kind, string? symbolId = null)
    {
        if (symbolId is not null && _occupiedSymbolIds.Contains(symbolId))
        {
            return GetThemeBrush("SignalRedBrush", Brushes.LightCoral);
        }

        return kind switch
        {
            TrackSymbolKind.Signal => GetThemeBrush("TrackPlanSignalBrush", Brushes.Red),
            TrackSymbolKind.Switch => GetThemeBrush("TrackPlanSwitchBrush", Brushes.Blue),
            TrackSymbolKind.DoubleSlipSwitch => GetThemeBrush("TrackPlanDoubleSlipSwitchBrush", Brushes.LightSkyBlue),
            TrackSymbolKind.TrackBlock => GetThemeBrush("TrackPlanBlockBrush", Brushes.Yellow),
            TrackSymbolKind.BufferStop => GetThemeBrush("TrackPlanSafetyBrush", Brushes.LightCoral),
            TrackSymbolKind.LevelCrossing or TrackSymbolKind.Uncoupler or TrackSymbolKind.Sensor => GetThemeBrush("TrackPlanAccessoryBrush", Brushes.LightGray),
            TrackSymbolKind.TunnelPortal or TrackSymbolKind.Bridge or TrackSymbolKind.Platform or TrackSymbolKind.Depot or TrackSymbolKind.Turntable => GetThemeBrush("TrackPlanStructureBrush", Brushes.Plum),
            _ => GetThemeBrush("TrackPlanDefaultSymbolBrush", Brushes.WhiteSmoke)
        };
    }

    private IBrush GetThemeBrush(string key, IBrush fallback)
    {
        if (this.TryGetResource(key, ActualThemeVariant, out var value) &&
            value is IBrush brush)
        {
            return brush;
        }

        return fallback;
    }

    private static string GetSymbolIcon(TrackSymbolKind kind)
    {
        return kind switch
        {
            TrackSymbolKind.Signal => "SIG",
            TrackSymbolKind.Switch => "W",
            TrackSymbolKind.DoubleSlipSwitch => "DKW",
            TrackSymbolKind.TrackBlock => "BLK",
            TrackSymbolKind.LevelCrossing => "BUE",
            TrackSymbolKind.TunnelPortal => "TUN",
            TrackSymbolKind.Bridge => "BR",
            TrackSymbolKind.Uncoupler => "ENT",
            TrackSymbolKind.Sensor => "M",
            TrackSymbolKind.Platform => "BST",
            TrackSymbolKind.BufferStop => "PR",
            TrackSymbolKind.Depot => "BW",
            TrackSymbolKind.Turntable => "DS",
            TrackSymbolKind.TextLabel => "TXT",
            _ => "GL"
        };
    }

    private static string GetSymbolIcon(DrawnTrackSymbol symbol)
    {
        if (symbol.Kind is not TrackSymbolKind.Switch)
        {
            return GetSymbolIcon(symbol.Kind);
        }

        return GetSwitchOrientation(symbol) switch
        {
            SwitchOrientation.LeftRightDown => "W v>",
            SwitchOrientation.RightLeftUp => "<^ W",
            SwitchOrientation.RightLeftDown => "<v W",
            _ => "W ^>"
        };
    }

    private static string GetSymbolCaption(DrawnTrackSymbol symbol)
    {
        return symbol.Kind is TrackSymbolKind.Signal
            ? $"{symbol.Name} {GetSignalArrow(symbol.SignalDirection)}"
            : symbol.Name;
    }

    private static string GetSignalArrow(SignalDirection direction)
    {
        return direction switch
        {
            SignalDirection.LeftToRight => ">",
            SignalDirection.RightToLeft => "<",
            SignalDirection.TopToBottom => "v",
            SignalDirection.BottomToTop => "^",
            _ => "<>"
        };
    }

    private static string GetSignalDirectionText(SignalDirection direction)
    {
        return direction switch
        {
            SignalDirection.Both => "Beide Richtungen",
            SignalDirection.LeftToRight => "Links nach rechts",
            SignalDirection.RightToLeft => "Rechts nach links",
            SignalDirection.TopToBottom => "Oben nach unten",
            SignalDirection.BottomToTop => "Unten nach oben",
            _ => direction.ToString()
        };
    }

    private static string GetSwitchPositionText(SwitchPosition position)
    {
        return position switch
        {
            SwitchPosition.Straight => "G",
            SwitchPosition.Diverging => "A",
            SwitchPosition.Left => "L",
            SwitchPosition.Right => "R",
            _ => position.ToString()
        };
    }

    private static string GetIltisSwitchPositionText(SwitchPosition position)
    {
        return position switch
        {
            SwitchPosition.Straight => "G",
            SwitchPosition.Diverging => "A",
            SwitchPosition.Left => "L",
            SwitchPosition.Right => "R",
            _ => "?"
        };
    }

    private enum TrackEditorTool
    {
        Select,
        Signal,
        Track,
        TrackBlock,
        SwitchLeftRightUp,
        SwitchLeftRightDown,
        SwitchRightLeftUp,
        SwitchRightLeftDown,
        DoubleSlipSwitch,
        LevelCrossing,
        TunnelPortal,
        Bridge,
        Uncoupler,
        Sensor,
        Platform,
        BufferStop,
        Depot,
        Turntable,
        TextLabel,
        Connect
    }

    private static bool TryGetKindForTool(TrackEditorTool tool, out TrackSymbolKind kind)
    {
        kind = tool switch
        {
            TrackEditorTool.Signal => TrackSymbolKind.Signal,
            TrackEditorTool.Track => TrackSymbolKind.Track,
            TrackEditorTool.TrackBlock => TrackSymbolKind.TrackBlock,
            TrackEditorTool.SwitchLeftRightUp
                or TrackEditorTool.SwitchLeftRightDown
                or TrackEditorTool.SwitchRightLeftUp
                or TrackEditorTool.SwitchRightLeftDown => TrackSymbolKind.Switch,
            TrackEditorTool.DoubleSlipSwitch => TrackSymbolKind.DoubleSlipSwitch,
            TrackEditorTool.LevelCrossing => TrackSymbolKind.LevelCrossing,
            TrackEditorTool.TunnelPortal => TrackSymbolKind.TunnelPortal,
            TrackEditorTool.Bridge => TrackSymbolKind.Bridge,
            TrackEditorTool.Uncoupler => TrackSymbolKind.Uncoupler,
            TrackEditorTool.Sensor => TrackSymbolKind.Sensor,
            TrackEditorTool.Platform => TrackSymbolKind.Platform,
            TrackEditorTool.BufferStop => TrackSymbolKind.BufferStop,
            TrackEditorTool.Depot => TrackSymbolKind.Depot,
            TrackEditorTool.Turntable => TrackSymbolKind.Turntable, 
            TrackEditorTool.TextLabel => TrackSymbolKind.TextLabel,
            _ => default
        };

        return tool is not (TrackEditorTool.Select or TrackEditorTool.Connect);
    }

    private sealed record PaletteElement(
        TrackSymbolKind Kind,
        TrackEditorTool Tool,
        string Label);

    private sealed record DemoSetting(
        string Key,
        string Label,
        string EnabledValue);

    private static readonly IBrush IltisBackgroundBrush = new SolidColorBrush(Color.Parse("#050505"));
    private static readonly IBrush IltisPanelBrush = new SolidColorBrush(Color.Parse("#111111"));
    private static readonly IBrush IltisRailBrush = new SolidColorBrush(Color.Parse("#B7B7B7"));
    private static readonly IBrush IltisInactiveRailBrush = new SolidColorBrush(Color.Parse("#555555"));
    private static readonly IBrush IltisGreenBrush = new SolidColorBrush(Color.Parse("#00E060"));
    private static readonly IBrush IltisRedBrush = new SolidColorBrush(Color.Parse("#FF3030"));
    private static readonly IBrush IltisYellowBrush = new SolidColorBrush(Color.Parse("#FFD200"));
    private static readonly IBrush IltisSelectionBrush = new SolidColorBrush(Color.Parse("#00A8FF"));
    private static readonly IBrush IltisTextBrush = new SolidColorBrush(Color.Parse("#E8E8E8"));
    private static readonly IBrush IltisDarkTextBrush = new SolidColorBrush(Color.Parse("#080808"));

    private const double PortSize = 22;

    private const string SwitchOrientationProperty = "SwitchOrientation";

    private static class SwitchOrientation
    {
        public const string LeftRightUp = "LeftRightUp";
        public const string LeftRightDown = "LeftRightDown";
        public const string RightLeftUp = "RightLeftUp";
        public const string RightLeftDown = "RightLeftDown";
    }

    private static void ApplyToolDefaults(DrawnTrackSymbol symbol, TrackEditorTool tool)
    {
        if (symbol.Kind is not TrackSymbolKind.Switch)
        {
            return;
        }

        symbol.Properties[SwitchOrientationProperty] = tool switch
        {
            TrackEditorTool.SwitchLeftRightDown => SwitchOrientation.LeftRightDown,
            TrackEditorTool.SwitchRightLeftUp => SwitchOrientation.RightLeftUp,
            TrackEditorTool.SwitchRightLeftDown => SwitchOrientation.RightLeftDown,
            _ => SwitchOrientation.LeftRightUp
        };
    }

    private static string GetSwitchOrientation(DrawnTrackSymbol symbol)
    {
        return symbol.Properties.TryGetValue(SwitchOrientationProperty, out var orientation) &&
               !string.IsNullOrWhiteSpace(orientation)
            ? orientation
            : SwitchOrientation.LeftRightUp;
    }

    private static string GetSwitchPortForNeighbor(DrawnTrackSymbol switchSymbol, DrawnTrackSymbol neighbor)
    {
        var orientation = GetSwitchOrientation(switchSymbol);
        var dx = neighbor.X - switchSymbol.X;
        var dy = neighbor.Y - switchSymbol.Y;
        var isLeft = dx < 0;
        var isRight = dx >= 0;
        var isAbove = dy < -20;
        var isBelow = dy > 20;

        return orientation switch
        {
            SwitchOrientation.LeftRightUp when isAbove => "C",
            SwitchOrientation.LeftRightUp when isRight => "B",
            SwitchOrientation.LeftRightUp => "A",
            SwitchOrientation.LeftRightDown when isBelow => "C",
            SwitchOrientation.LeftRightDown when isRight => "B",
            SwitchOrientation.LeftRightDown => "A",
            SwitchOrientation.RightLeftUp when isAbove => "C",
            SwitchOrientation.RightLeftUp when isLeft => "B",
            SwitchOrientation.RightLeftUp => "A",
            SwitchOrientation.RightLeftDown when isBelow => "C",
            SwitchOrientation.RightLeftDown when isLeft => "B",
            SwitchOrientation.RightLeftDown => "A",
            _ => isRight ? "B" : "A"
        };
    }

    private static SwitchGeometry GetSwitchGeometry(DrawnTrackSymbol symbol)
    {
        return GetSwitchOrientation(symbol) switch
        {
            SwitchOrientation.LeftRightDown => new(
                new Point(4, 22),
                new Point(92, 22),
                new Point(38, 22),
                new Point(84, 38)),
            SwitchOrientation.RightLeftUp => new(
                new Point(92, 22),
                new Point(4, 22),
                new Point(58, 22),
                new Point(12, 6)),
            SwitchOrientation.RightLeftDown => new(
                new Point(92, 22),
                new Point(4, 22),
                new Point(58, 22),
                new Point(12, 38)),
            _ => new(
                new Point(4, 22),
                new Point(92, 22),
                new Point(38, 22),
                new Point(84, 6))
        };
    }

    private sealed record SwitchGeometry(
        Point StraightStart,
        Point StraightEnd,
        Point DivergingStart,
        Point DivergingEnd);

    private sealed record SymbolPort(
        DrawnTrackSymbol Symbol,
        string Name);
}
