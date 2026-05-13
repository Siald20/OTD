using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OTD.TrackPlan;
using OTD.TrackPlan.Interlocking;
using OTD.TrackPlan.Interlocking.Profiles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using IOPath = System.IO.Path;

namespace OTD.Views;

public partial class TrackPlanPage : UserControl
{
    private readonly Action? _navigateBack;

    private readonly List<PaletteElement> _paletteElements =
    [
        new(TrackSymbolKind.Signal, TrackEditorTool.Signal, "Signal"),
        new(TrackSymbolKind.ZwergSignal, TrackEditorTool.ZwergSignal, "Zwergsignal"),
        new(TrackSymbolKind.Track, TrackEditorTool.Track, "Gleis"),
        new(TrackSymbolKind.LineBlock, TrackEditorTool.LineBlock, "Streckenblock"),
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
    private readonly string _dataDirectory = ResolveDataDirectory();
    private readonly string _stationsDirectory;
    private readonly string _stationsConfigPath;
    private readonly Dictionary<string, StationContext> _stations = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<BoundaryLinkEntry> _boundaryLinks = [];
    private readonly Dictionary<string, DateTimeOffset> _boundaryHeartbeat = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeStationId;
    private string _planFilePath = string.Empty;
    private string _routesFilePath = string.Empty;
    private TrackPlanEditorModel _trackPlanEditor = new(new TrackPlanDocument());
    private readonly RouteBuilder _routeBuilder = new();
    private StationInterlockingRuntime _interlockingRuntime = new(new Domino67InterlockingProfile());
    private readonly HashSet<string> _highlightedConnectionKeys = [];
    private readonly HashSet<string> _activeRouteConnectionKeys = [];
    private readonly HashSet<string> _releasedRouteConnectionKeys = [];
    private List<RouteResult> _visibleRoutes = [];
    private SettingsWindow? _settingsWindow;
    private TrackEditorTool _activeTool = TrackEditorTool.Select;
    private DrawnTrackSymbol? _selectedOperationSymbol;
    private DrawnTrackSymbol? _operationStartSymbol;
    private RouteResult? _selectedOperationRoute;
    private DrawnTrackSymbol? _connectionStart;
    private SymbolPort? _connectionStartPort;
    private DrawnTrackSymbol? _draggedSymbol;
    private Point _dragOffset;
    private bool _isDragging;
    private bool _isOperationMode;
    private DrawnTrackSymbol? _selectedBoundarySourceLineBlock;
    private readonly DispatcherTimer _storedRouteRetryTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private static readonly TimeSpan BoundaryCommunicationTimeout = TimeSpan.FromMinutes(5);

    private HashSet<string> _occupiedSymbolIds => _interlockingRuntime.OccupiedSymbolIds;
    private HashSet<string> _releaseOnFreeSymbolIds => _interlockingRuntime.ReleaseOnFreeSymbolIds;
    private HashSet<string> _greenSignalIds => _interlockingRuntime.GreenSignalIds;
    private HashSet<string> _lockedSymbolIds => _interlockingRuntime.LockedSymbolIds;
    private List<RouteResult> _activeRoutes => _interlockingRuntime.ActiveRoutes;
    private List<RouteResult> _storedRoutes => _interlockingRuntime.StoredRoutes;

    public TrackPlanPage()
        : this(null)
    {
    }

    public TrackPlanPage(Action? navigateBack)
    {
        _navigateBack = navigateBack;
        _stationsDirectory = IOPath.Combine(_dataDirectory, "Stations");
        _stationsConfigPath = IOPath.Combine(_stationsDirectory, "stations.xml");
        InitializeComponent();
        RenderPalette();
        LoadStations();
        RenderTrackPlan();
        _storedRouteRetryTimer.Tick += (_, _) =>
        {
            if (_storedRoutes.Count == 0)
            {
                return;
            }

            TrySetStoredRoutesBackground();
        };
        _storedRouteRetryTimer.Start();
    }

    private static string ResolveDataDirectory()
    {
        var searchDirectory = Environment.CurrentDirectory;
        for (var i = 0; i < 8; i++)
        {
            if (File.Exists(IOPath.Combine(searchDirectory, "OTD.csproj")))
            {
                return searchDirectory;
            }

            var parent = Directory.GetParent(searchDirectory);
            if (parent is null)
            {
                break;
            }

            searchDirectory = parent.FullName;
        }

        return Environment.CurrentDirectory;
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

    private TrackPlanDocument LoadTrackPlanDocument(string filePath)
    {
        try
        {
            return _trackPlanDocumentStore.Load(filePath);
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
        ClearGreenSignals();
        _lockedSymbolIds.Clear();
        _releaseOnFreeSymbolIds.Clear();
        _storedRoutes.Clear();
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
        _operationStartSymbol = null;
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
        _operationStartSymbol = null;
        TrackCanvas.Background = IltisBackgroundBrush;
        EditorPanel.IsVisible = false;
        EditorAutomationPanel.IsVisible = false;
        OperationPanel.IsVisible = true;
        EditorModeButton.Classes.Set("primary", false);
        OperationModeButton.Classes.Set("primary", true);
        if (_visibleRoutes.Count == 0)
        {
            PopulateRoutes();
        TrackPlanStatus.Text = "Bedienung aktiv. Start (Signal/Zwergsignal) anklicken, danach Ziel (Signal/Zwergsignal/Streckenblock/Prellbock).";
        }
        else
        {
            RefreshRouteLists();
        TrackPlanStatus.Text = "Bedienung aktiv. Start (Signal/Zwergsignal) anklicken, danach Ziel (Signal/Zwergsignal/Streckenblock/Prellbock).";
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

        var starts = _trackPlanEditor.Document.Symbols
            .Where(symbol => symbol.Kind is TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal)
            .ToList();

        if (starts.Count < 2)
        {
            TrackPlanStatus.Text = "Mindestens zwei Signal-/Zwergsignal-Elemente werden fuer eine Fahrstrasse benoetigt.";
            RenderTrackPlan();
            return;
        }

        var start = starts.First();
        var target = starts.Last();
        var routeType = start.Kind is TrackSymbolKind.ZwergSignal
            ? RouteType.Shunting
            : RouteType.Train;

        var graph = _trackPlanEditor.ToGraph();
        var result = _routeBuilder.FindRoute(graph, start.Id, target.Id, new RouteSearchOptions
        {
            RouteType = routeType,
            AllowOccupiedSymbols = routeType is RouteType.Shunting
        });

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
        _visibleRoutes.AddRange(_routeBuilder.FindAllRoutes(graph, new RouteSearchOptions
        {
            RouteType = RouteType.Train
        }));
        _visibleRoutes.AddRange(_routeBuilder.FindAllRoutes(graph, new RouteSearchOptions
        {
            RouteType = RouteType.Shunting,
            AllowOccupiedSymbols = true
        }));

        var deduplicated = _visibleRoutes
            .GroupBy(route => new { route.RouteType, StartId = route.StartSignal.Id, TargetId = route.TargetSignal.Id })
            .Select(group => group.OrderBy(route => route.Cost).First())
            .OrderBy(route => route.RouteType)
            .ThenBy(route => route.StartSignal.Name)
            .ThenBy(route => route.TargetSignal.Name)
            .ToList();
        _visibleRoutes.Clear();
        _visibleRoutes.AddRange(deduplicated);
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
        TryApplyRoute(_selectedOperationRoute, storeOnFailure: true);
    }

    private bool TryApplyRoute(RouteResult route, bool storeOnFailure)
    {
        var lineBlocksWithIncomingBeforeSet = route.Symbols
            .Where(symbol => symbol.Kind is TrackSymbolKind.LineBlock)
            .Select(symbol => FindSymbol(symbol.Id))
            .Where(static symbol => symbol is not null &&
                symbol.Properties.TryGetValue(Domino67PropertyNames.LineBlockDirection, out var dir) &&
                string.Equals(dir, Domino67PropertyNames.LineBlockDirectionIncoming, StringComparison.OrdinalIgnoreCase))
            .Cast<DrawnTrackSymbol>()
            .ToList();

        if (!CanSetRouteAcrossBoundaries(route, out var boundaryFailure))
        {
            if (storeOnFailure && CanStoreRoute(boundaryFailure))
            {
                StoreRoute(route, boundaryFailure);
            }
            else
            {
                TrackPlanStatus.Text = boundaryFailure;
            }

            RenderTrackPlan();
            return false;
        }

        if (!_interlockingRuntime.TryApplyRoute(
                route,
                _trackPlanEditor.Document,
                storeOnFailure,
                CanStoreRoute,
                OnDelayedActionApplied,
                out var message,
                out var switchCommands))
        {
            if (storeOnFailure && CanStoreRoute(message))
            {
                StoreRoute(route, message);
            }
            else
            {
                TrackPlanStatus.Text = message;
            }

            RenderTrackPlan();
            return false;
        }

        // Ab hier war das Stellen erfolgreich. Die UI uebernimmt nur noch das Ergebnis:
        // aktive Fahrwegmarkierung, Signalbegriffe und Statusmeldung.
        _releasedRouteConnectionKeys.Clear();
        foreach (var connection in route.Connections)
        {
            _activeRouteConnectionKeys.Add(GetConnectionKey(connection.FromSymbolId, connection.ToSymbolId));
            _activeRouteConnectionKeys.Add(GetConnectionKey(connection.ToSymbolId, connection.FromSymbolId));
        }

        foreach (var signalId in _greenSignalIds)
        {
            SetGreenSignal(signalId);
        }

        foreach (var lineBlock in lineBlocksWithIncomingBeforeSet)
        {
            TryResetLineBlockToGrundstellung(lineBlock);
            SyncBoundaryGrundstellung(lineBlock);
        }

        SyncBoundaryStateForRoute(route);
        EnsureRemoteIncomingDisplayForRoute(route);
        UpdateActiveRouteText();
        TrackPlanStatus.Text = message + " Weichenbefehle: " + FormatSwitchCommands(switchCommands);
        SaveTrackPlan();
        RenderTrackPlan();
        return true;
    }

    private void ReleaseRoute_OnClick(object? sender, RoutedEventArgs e)
    {
        var releasableRoutes = _activeRoutes
            .Where(route =>
            {
                var target = FindSymbol(route.TargetSignal.Id);
                return target is null || CanReleaseFromTargetSide(target);
            })
            .ToList();
        var blockedRoutes = _activeRoutes.Count - releasableRoutes.Count;
        if (releasableRoutes.Count == 0)
        {
            // Fallback gegen Deadlocks: wenn lokal aktive Routen existieren,
            // muessen sie lokal zwangsweise aufloesbar bleiben.
            releasableRoutes = _activeRoutes.ToList();
            blockedRoutes = 0;
        }

        var previouslyActiveRoutes = releasableRoutes.ToList();
        RemoveStoredRoutesMatching(previouslyActiveRoutes);
        foreach (var route in releasableRoutes)
        {
            _ = _interlockingRuntime.ReleaseRoutesContainingSymbol(
                route.TargetSignal.Id,
                _trackPlanEditor.Document,
                OnDelayedActionApplied);
        }
        _activeRouteConnectionKeys.Clear();
        _releasedRouteConnectionKeys.Clear();
        ClearGreenSignals();
        _lockedSymbolIds.Clear();
        _operationStartSymbol = null;
        RebuildLockedSymbols();
        TrackPlanStatus.Text = blockedRoutes > 0
            ? $"{releasableRoutes.Count} Fahrstrasse(n) aufgeloest, {blockedRoutes} wegen AN-Seite uebersprungen."
            : "Fahrstrasse aufgeloest.";
        SyncBoundaryGrundstellungForRoutes(previouslyActiveRoutes);
        SyncBoundaryStateForRoutes(previouslyActiveRoutes);
        TrySetStoredRoutes();
        UpdateActiveRouteText();
        RenderTrackPlan();
    }

    private void ToggleSelectedBlock_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedOperationSymbol is null ||
            _selectedOperationSymbol.Kind is not TrackSymbolKind.LineBlock)
        {
            TrackPlanStatus.Text = "Waehle zuerst in der Bedienebene einen Streckenblock aus.";
            return;
        }

        if (!_occupiedSymbolIds.Add(_selectedOperationSymbol.Id))
        {
            _occupiedSymbolIds.Remove(_selectedOperationSymbol.Id);
            if (_releaseOnFreeSymbolIds.Remove(_selectedOperationSymbol.Id))
            {
                ReleaseActiveRouteElement(_selectedOperationSymbol);
            }
            TryResetLineBlockToGrundstellung(_selectedOperationSymbol);
            SyncBoundaryGrundstellung(_selectedOperationSymbol);
            TrackPlanStatus.Text = $"{_selectedOperationSymbol.Name} ist frei.";
            SyncBoundaryState(_selectedOperationSymbol);
            TrySetStoredRoutes();
        }
        else
        {
            var affectedRoutes = _activeRoutes
                .Where(route => route.Symbols.Any(symbol => symbol.Id == _selectedOperationSymbol.Id))
                .ToList();
            foreach (var route in affectedRoutes)
            {
                ClearRouteSignals(route);
            }

            if (affectedRoutes.Count > 0)
            {
                var releaseTrigger = GetReleaseTrigger(_selectedOperationSymbol);
                if (releaseTrigger is ReleaseTrigger.OnOccupy)
                {
                    ReleaseActiveRouteElement(_selectedOperationSymbol);
                }
                else if (releaseTrigger is ReleaseTrigger.OnFreeAfterOccupy)
                {
                    _releaseOnFreeSymbolIds.Add(_selectedOperationSymbol.Id);
                }
            }

            TrackPlanStatus.Text = $"{_selectedOperationSymbol.Name} ist belegt.";
            SyncBoundaryState(_selectedOperationSymbol);
        }

        RenderTrackPlan();
    }

    private void EmergencyStop_OnClick(object? sender, RoutedEventArgs e)
    {
        var previouslyActiveRoutes = _activeRoutes.ToList();
        _activeRoutes.Clear();
        _activeRouteConnectionKeys.Clear();
        _releasedRouteConnectionKeys.Clear();
        ClearGreenSignals();
        _lockedSymbolIds.Clear();
        _releaseOnFreeSymbolIds.Clear();
        _storedRoutes.Clear();
        _highlightedConnectionKeys.Clear();
        _operationStartSymbol = null;
        UpdateActiveRouteText();
        TrackPlanStatus.Text = "Not-Halt ausgeloest.";
        SyncBoundaryStateForRoutes(previouslyActiveRoutes);
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
        _releaseOnFreeSymbolIds.Clear();
        ClearGreenSignals();
        _lockedSymbolIds.Clear();
        _activeRoutes.Clear();
        _storedRoutes.Clear();
        _visibleRoutes.Clear();
        RouteList.Items.Clear();
        OperationRouteList.Items.Clear();
        _selectedOperationRoute = null;
        _selectedOperationSymbol = null;

        var signalA = _trackPlanEditor.AddSymbol(TrackSymbolKind.Signal, 80, 220, "Signal A");
        signalA.SignalDirection = SignalDirection.LeftToRight;
        var switch1 = _trackPlanEditor.AddSymbol(TrackSymbolKind.Switch, 250, 220, "Weiche 1");
        switch1.Properties[SwitchOrientationProperty] = SwitchOrientation.LeftRightUp;
        var track1 = _trackPlanEditor.AddSymbol(TrackSymbolKind.LineBlock, 430, 160, "Block 1");
        var track2 = _trackPlanEditor.AddSymbol(TrackSymbolKind.LineBlock, 430, 280, "Block 2");
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
            var isActiveShunting = _activeRoutes.Any(route =>
                route.RouteType is RouteType.Shunting &&
                route.Connections.Any(connection =>
                    (connection.FromSymbolId == from.Id && connection.ToSymbolId == to.Id) ||
                    (connection.FromSymbolId == to.Id && connection.ToSymbolId == from.Id)));
            var isHighlighted = _highlightedConnectionKeys.Contains(connectionKey);
            var line = new Line
            {
                StartPoint = GetPortPoint(from, connection.FromPort),
                EndPoint = GetPortPoint(to, connection.ToPort),
                Stroke = isReleased
                    ? GetThemeBrush("TrackPlanRailBrush", Brushes.DimGray)
                    : isActive
                    ? (isActiveShunting
                        ? GetThemeBrush("TrackPlanRouteHighlightBrush", Brushes.DeepSkyBlue)
                        : GetThemeBrush("SignalGreenBrush", Brushes.LimeGreen))
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
                ApplyIltisConnectionStyle(line, isReleased, isActive, isHighlighted, isActiveShunting);
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
            TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal => CreateOperationSignal(symbol),
            TrackSymbolKind.LineBlock => CreateOperationBlock(symbol),
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
        var signalBrush = _greenSignalIds.Contains(symbol.Id) ||
                          IsPropertyEnabled(symbol.Id, Domino67PropertyNames.SignalIsGreen)
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
        var caption = symbol.Kind is TrackSymbolKind.LineBlock
            ? GetSymbolCaption(symbol)
            : symbol.Name;

        return new Border
        {
            Width = symbol.Kind is TrackSymbolKind.LineBlock ? 146 : 104,
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
                Text = caption,
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
        bool isHighlighted,
        bool isActiveShunting)
    {
        line.Stroke = isActive
            ? (isActiveShunting ? IltisSelectionBrush : IltisGreenBrush)
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
            Background = GetOperationAccessoryBrush(symbol),
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

    private IBrush GetOperationAccessoryBrush(DrawnTrackSymbol symbol)
    {
        if (symbol.Kind is TrackSymbolKind.LevelCrossing)
        {
            return IsPropertyEnabled(symbol.Id, Domino67PropertyNames.LevelCrossingClosed)
                ? IltisGreenBrush
                : IltisRedBrush;
        }

        return IltisPanelBrush;
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
            if (symbol.Kind is TrackSymbolKind.LineBlock)
            {
                var setIncomingItem = new MenuItem { Header = "Auf AN stellen" };
                setIncomingItem.Click += (_, _) => SetLineBlockToIncoming(symbol);
                menu.Items.Add(setIncomingItem);

                var resetItem = new MenuItem { Header = "Grundstellung" };
                resetItem.Click += (_, _) => SetLineBlockToGrundstellung(symbol);
                menu.Items.Add(resetItem);
            }

            if (IsOperationRouteEndpoint(symbol) &&
                _activeRoutes.Any(route => route.TargetSignal.Id == symbol.Id))
            {
                var releaseRouteItem = new MenuItem { Header = "Fahrstrasse aufloesen" };
                releaseRouteItem.Click += (_, _) => ReleaseRoutesByTargetSymbol(symbol);
                menu.Items.Add(new Separator());
                menu.Items.Add(releaseRouteItem);
            }

            return menu;
        }

        menu.Items.Add(renameItem);
        menu.Items.Add(connectItem);
        menu.Items.Add(new Separator());

        if (symbol.Kind is TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal or TrackSymbolKind.LineBlock)
        {
            var directionMenu = new MenuItem
            {
                Header = symbol.Kind is TrackSymbolKind.LineBlock ? "Blockrichtung" : "Signalrichtung"
            };
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

        if (symbol.Kind is TrackSymbolKind.LineBlock)
        {
            _selectedBoundarySourceLineBlock = symbol;
        }

        if (e.GetCurrentPoint(TrackCanvas).Properties.IsRightButtonPressed)
        {
            return;
        }

        e.Handled = true;

        if (_isOperationMode)
        {
            if (IsOperationRouteEndpoint(symbol))
            {
                SelectOperationSignal(symbol);
                return;
            }

            _selectedOperationSymbol = symbol;
            TrackPlanStatus.Text = symbol.Kind is TrackSymbolKind.LineBlock
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

        if (_operationStartSymbol is null || _operationStartSymbol.Id == signal.Id)
        {
            if (signal.Kind is not (TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal))
            {
                TrackPlanStatus.Text = "Start muss ein Signal oder Zwergsignal sein.";
                RenderTrackPlan();
                return;
            }

            _operationStartSymbol = signal;
            _selectedOperationRoute = null;
            _highlightedConnectionKeys.Clear();
            TrackPlanStatus.Text = $"{signal.Name} als Start gewaehlt. Ziel (Signal/Zwergsignal/Streckenblock/Prellbock) anklicken.";
            RenderTrackPlan();
            return;
        }

        var routeType = _operationStartSymbol.Kind is TrackSymbolKind.ZwergSignal
            ? RouteType.Shunting
            : RouteType.Train;
        var route = FindOperationRoute(_operationStartSymbol.Id, signal.Id, routeType);
        if (route is null)
        {
            TrackPlanStatus.Text = $"Keine Fahrstrasse von {_operationStartSymbol.Name} nach {signal.Name} gefunden.";
            _operationStartSymbol = null;
            _selectedOperationRoute = null;
            _highlightedConnectionKeys.Clear();
            RenderTrackPlan();
            return;
        }

        _selectedOperationRoute = route;
        _operationStartSymbol = null;
        HighlightRoute(route);
        SetSelectedOperationRoute();
    }

    private RouteResult? FindOperationRoute(string startSignalId, string targetSignalId, RouteType routeType)
    {
        var existingRoute = _visibleRoutes.FirstOrDefault(route =>
            route.StartSignal.Id == startSignalId &&
            route.TargetSignal.Id == targetSignalId &&
            route.RouteType == routeType);

        if (existingRoute is not null)
        {
            return existingRoute;
        }

        var graph = _trackPlanEditor.ToGraph();
        var result = _routeBuilder.FindRoute(graph, startSignalId, targetSignalId, new RouteSearchOptions
        {
            RouteType = routeType,
            AllowOccupiedSymbols = routeType is RouteType.Shunting
        });
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

        var menu = new ContextMenu
        {
            Items =
            {
                removeItem
            }
        };
        return menu;
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
        ClearGreenSignals();
        _lockedSymbolIds.Clear();
        _releaseOnFreeSymbolIds.Clear();
        _storedRoutes.Clear();
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
        if (symbol.Kind is TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal or TrackSymbolKind.LineBlock)
        {
            content.Children.Add(CreateSectionTitle(symbol.Kind is TrackSymbolKind.LineBlock ? "Streckenblock" : "Signal"));
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
                    if (symbol.Kind is TrackSymbolKind.LineBlock)
                    {
                        symbol.Properties[Domino67PropertyNames.LineBlockTravelDirection] = direction.ToString();
                    }
                }
            };

            content.Children.Add(CreateLabeledControl(
                symbol.Kind is TrackSymbolKind.LineBlock ? "Blockrichtung" : "Signalrichtung",
                signalDirectionBox));
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
        content.Children.Add(CreatePropertyCheckBox(symbol, new DemoSetting(Domino67PropertyNames.TrackClosed, "Element im Domino 67 sperren", "true")));

        if (symbol.Kind is TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal)
        {
            content.Children.Add(CreatePropertyCheckBox(symbol, new DemoSetting(Domino67PropertyNames.HoldRed, "Startsignal auf Halt halten", "true")));
            content.Children.Add(CreatePropertyCheckBox(symbol, new DemoSetting(Domino67PropertyNames.SignalKeepGreenOnRelease, "Bei Aufloesung gruen belassen", "true")));
            content.Children.Add(CreatePropertyTextBox(symbol, Domino67PropertyNames.FlankProtectionSymbols, "Flankenschutz-Symbole", "Symbol-IDs mit Komma trennen"));
            content.Children.Add(CreatePropertyTextBox(symbol, Domino67PropertyNames.OverlapSymbols, "Durchrutschweg-Symbole", "Symbol-IDs mit Komma trennen"));
        }

        if (symbol.Kind is TrackSymbolKind.LineBlock)
        {
            content.Children.Add(CreatePropertyTextBox(symbol, Domino67PropertyNames.OverlapSymbols, "Durchrutschweg-Symbole", "Symbol-IDs mit Komma trennen"));

            var releaseTriggerBox = new ComboBox
            {
                MinWidth = 240
            };
            releaseTriggerBox.Items.Add("Manuell");
            releaseTriggerBox.Items.Add("Bei Belegung");
            releaseTriggerBox.Items.Add("Bei Frei nach Belegung");

            releaseTriggerBox.SelectedItem = symbol.Properties.TryGetValue(Domino67PropertyNames.ReleaseTrigger, out var trigger)
                ? trigger switch
                {
                    "on_occupy" => "Bei Belegung",
                    "on_free_after_occupy" => "Bei Frei nach Belegung",
                    _ => "Manuell"
                }
                : "Manuell";

            releaseTriggerBox.SelectionChanged += (_, _) =>
            {
                if (releaseTriggerBox.SelectedItem is not string selected ||
                    string.Equals(selected, "Manuell", StringComparison.OrdinalIgnoreCase))
                {
                    symbol.Properties.Remove(Domino67PropertyNames.ReleaseTrigger);
                    return;
                }

                symbol.Properties[Domino67PropertyNames.ReleaseTrigger] = selected switch
                {
                    "Bei Belegung" => "on_occupy",
                    "Bei Frei nach Belegung" => "on_free_after_occupy",
                    _ => "manual"
                };
            };

            content.Children.Add(CreateLabeledControl("Aufloesen", releaseTriggerBox));
        }

        if (symbol.Kind is TrackSymbolKind.Switch or TrackSymbolKind.DoubleSlipSwitch)
        {
          
            content.Children.Add(CreatePropertyCheckBox(symbol, new DemoSetting(Domino67PropertyNames.SwitchLocked, "Weiche verschlossen", "true")));
            content.Children.Add(CreatePropertyCheckBox(symbol, new DemoSetting(Domino67PropertyNames.FlankProtectionEnabled, "Als Schutzweiche verwenden", "true")));
            content.Children.Add(CreatePropertyCheckBox(symbol, new DemoSetting(Domino67PropertyNames.SwitchReleaseToRequiredPosition, "Bei Aufloesung auf Pflichtlage", "true")));

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
            content.Children.Add(CreatePropertyTextBox(symbol, Domino67PropertyNames.LevelCrossingAutoCloseDelaySeconds, "Auto-Close-Verzoegerung (s)", "z.B. 10"));
            content.Children.Add(CreatePropertyCheckBox(symbol, new DemoSetting(Domino67PropertyNames.LevelCrossingKeepClosedOnRelease, "Bei Aufloesung geschlossen lassen", "true")));
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
            TrackSymbolKind.LineBlock => [new DemoSetting("Demo.ReserveOnly", "Nur Reservemanöver", "true")],
            TrackSymbolKind.Signal => [new DemoSetting("Demo.HoldRed", "Signal auf Halt halten", "true")],
            TrackSymbolKind.ZwergSignal => [new DemoSetting("Demo.HoldRed", "Zwergsignal auf Halt halten", "true")],
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
        ClearGreenSignals();
        _lockedSymbolIds.Clear();
        _releaseOnFreeSymbolIds.Clear();
        _storedRoutes.Clear();
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
        if (!CanReleaseFromTargetSide(symbol))
        {
            TrackPlanStatus.Text = $"{symbol.Name} kann nur auf AB-Seite aufgeloest werden.";
            return;
        }

        var affectedRoutes = _interlockingRuntime.ReleaseRoutesContainingSymbol(
            symbol.Id,
            _trackPlanEditor.Document,
            OnDelayedActionApplied);

        if (affectedRoutes.Count == 0)
        {
            return;
        }

        RemoveStoredRoutesMatching(affectedRoutes);

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
            ClearRouteSignals(route);
        }

        RebuildLockedSymbols();
        UpdateActiveRouteText();

        if (_activeRoutes.Count == 0)
        {
            _releasedRouteConnectionKeys.Clear();
        }

        SyncBoundaryGrundstellungForRoutes(affectedRoutes);
        SyncBoundaryStateForRoutes(affectedRoutes);
        TrySetStoredRoutes();
    }

    private void ReleaseRoutesByTargetSymbol(DrawnTrackSymbol targetSymbol)
    {
        var affectedRoutes = _activeRoutes
            .Where(route => route.TargetSignal.Id == targetSymbol.Id)
            .ToList();
        if (affectedRoutes.Count == 0)
        {
            TrackPlanStatus.Text = $"Keine aktive Fahrstrasse mit Ziel {targetSymbol.Name}.";
            return;
        }

        if (!CanReleaseFromTargetSide(targetSymbol))
        {
            TrackPlanStatus.Text = $"{targetSymbol.Name}: Zwangsaufloesung aktiv (AN-Seite).";
        }

        var allReleased = new List<RouteResult>();
        foreach (var route in affectedRoutes)
        {
            var released = _interlockingRuntime.ReleaseRoutesContainingSymbol(
                route.TargetSignal.Id,
                _trackPlanEditor.Document,
                OnDelayedActionApplied);
            allReleased.AddRange(released);
        }
        RemoveStoredRoutesMatching(allReleased);

        foreach (var connection in allReleased.SelectMany(route => route.Connections))
        {
            _activeRouteConnectionKeys.Remove(GetConnectionKey(connection.FromSymbolId, connection.ToSymbolId));
            _activeRouteConnectionKeys.Remove(GetConnectionKey(connection.ToSymbolId, connection.FromSymbolId));
            _releasedRouteConnectionKeys.Add(GetConnectionKey(connection.FromSymbolId, connection.ToSymbolId));
            _releasedRouteConnectionKeys.Add(GetConnectionKey(connection.ToSymbolId, connection.FromSymbolId));
        }

        foreach (var route in allReleased)
        {
            ClearRouteSignals(route);
        }

        RebuildLockedSymbols();
        UpdateActiveRouteText();
        if (_activeRoutes.Count == 0)
        {
            _releasedRouteConnectionKeys.Clear();
        }

        TrackPlanStatus.Text = $"{allReleased.Count} Fahrstrasse(n) am Ziel {targetSymbol.Name} aufgeloest.";
        SyncBoundaryGrundstellungForRoutes(allReleased);
        SyncBoundaryStateForRoutes(allReleased);
        TrySetStoredRoutes();
        SaveTrackPlan();
        RenderTrackPlan();
    }

    private static bool CanReleaseFromTargetSide(DrawnTrackSymbol symbol)
    {
        if (symbol.Kind is not TrackSymbolKind.LineBlock)
        {
            return true;
        }

        return symbol.Properties.TryGetValue(Domino67PropertyNames.LineBlockDirection, out var direction) &&
               string.Equals(direction, Domino67PropertyNames.LineBlockDirectionOutgoing, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateActiveRouteText()
    {
        ActiveRouteText.Text = _activeRoutes.Count == 0
            ? "Keine Fahrstrasse aktiv."
            : $"{_activeRoutes.Count} Fahrstrassen aktiv.";

        StoredRouteText.Text = _storedRoutes.Count == 0
            ? "Fahrstrassenspeicher leer."
            : $"Fahrstrassenspeicher aktiv: {string.Join(", ", _storedRoutes.Select(FormatRouteName))}";
    }

    private void StoreRoute(RouteResult route, string reason)
    {
        if (_storedRoutes.Any(storedRoute => IsSameRoute(storedRoute, route)))
        {
            TrackPlanStatus.Text = $"{FormatRouteName(route)} bereits im Fahrstrassenspeicher. Grund: {reason}";
            UpdateActiveRouteText();
            return;
        }

        _storedRoutes.Add(route);
        TrackPlanStatus.Text = $"{FormatRouteName(route)} im Fahrstrassenspeicher. Grund: {reason}";
        UpdateActiveRouteText();
    }

    private void TrySetStoredRoutes()
    {
        if (_storedRoutes.Count == 0)
        {
            return;
        }

        var storedCount = _storedRoutes.Count;
        var setCount = 0;
        foreach (var route in _storedRoutes.ToList())
        {
            if (TryApplyRoute(route, storeOnFailure: false))
            {
                setCount++;
            }
        }

        if (setCount > 0)
        {
            TrackPlanStatus.Text = setCount == storedCount
                ? "Alle gespeicherten Fahrstrassen wurden gestellt."
                : $"{setCount} gespeicherte Fahrstrasse(n) gestellt, {_storedRoutes.Count} bleiben gespeichert.";
        }

        UpdateActiveRouteText();
    }

    private void TrySetStoredRoutesBackground()
    {
        if (_storedRoutes.Count == 0)
        {
            return;
        }

        var setCount = _interlockingRuntime.TrySetStoredRoutes(
            _trackPlanEditor.Document,
            CanStoreRoute,
            OnDelayedActionApplied);
        if (setCount <= 0)
        {
            return;
        }

        // Zustand fachlich aktualisieren, aber ohne UI-Text/Neurendern,
        // damit offene Kontextmenues stabil bleiben.
        RebuildActiveRouteConnectionKeys();
        SaveTrackPlan();
    }

    private void OnDelayedActionApplied(RouteSettingContext context, RouteSettingResultBuilder delayedResult)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var signalId in delayedResult.GreenSignalIds)
            {
                SetGreenSignal(signalId);
            }

            SaveTrackPlan();
            RenderTrackPlan();
        });
    }

    private static ReleaseTrigger GetReleaseTrigger(DrawnTrackSymbol symbol)
    {
        if (!symbol.Properties.TryGetValue(Domino67PropertyNames.ReleaseTrigger, out var trigger) ||
            string.IsNullOrWhiteSpace(trigger))
        {
            return ReleaseTrigger.Manual;
        }

        return trigger.Trim().ToLowerInvariant() switch
        {
            "on_occupy" => ReleaseTrigger.OnOccupy,
            "on_free_after_occupy" => ReleaseTrigger.OnFreeAfterOccupy,
            _ => ReleaseTrigger.Manual
        };
    }

    private void SetGreenSignal(string signalId)
    {
        _greenSignalIds.Add(signalId);
        SetSymbolProperty(signalId, Domino67PropertyNames.SignalIsGreen, true);
    }

    private void ClearRouteSignals(RouteResult route)
    {
        foreach (var symbol in route.Symbols)
        {
            if (symbol.Kind is TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal)
            {
                ClearGreenSignal(symbol.Id);
            }
        }
    }

    private void ClearGreenSignal(string signalId)
    {
        _greenSignalIds.Remove(signalId);
        SetSymbolProperty(signalId, Domino67PropertyNames.SignalIsGreen, false);
    }

    private void ClearGreenSignals()
    {
        foreach (var signal in _trackPlanEditor.Document.Symbols.Where(static s => s.Kind is TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal))
        {
            SetSymbolProperty(signal.Id, Domino67PropertyNames.SignalIsGreen, false);
        }

        _greenSignalIds.Clear();
    }

    private void SetSymbolProperty(string symbolId, string propertyName, bool value)
    {
        var symbol = FindSymbol(symbolId);
        if (symbol is not null)
        {
            symbol.Properties[propertyName] = value.ToString().ToLowerInvariant();
        }
    }

    private bool IsPropertyEnabled(string symbolId, string propertyName)
    {
        var symbol = FindSymbol(symbolId);
        return symbol is not null &&
               Domino67PropertyHelper.IsEnabled(symbol, propertyName);
    }

    private static bool CanStoreRoute(string message)
    {
        return message.Contains("belegt", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("verschlossen", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("gesperrt", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("kollidiert", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("verriegelt", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("AB", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("grenzblock", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("gegenrichtung", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameRoute(RouteResult left, RouteResult right)
    {
        return left.StartSignal.Id == right.StartSignal.Id &&
               left.TargetSignal.Id == right.TargetSignal.Id &&
               left.RouteType == right.RouteType;
    }

    private void RemoveStoredRoutesMatching(IEnumerable<RouteResult> releasedRoutes)
    {
        var released = releasedRoutes.ToList();
        if (released.Count == 0 || _storedRoutes.Count == 0)
        {
            return;
        }

        _storedRoutes.RemoveAll(stored =>
            released.Any(route => IsSameRoute(stored, route)));
    }

    private static bool IsOperationRouteEndpoint(DrawnTrackSymbol symbol)
    {
        return symbol.Kind is TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal or TrackSymbolKind.LineBlock or TrackSymbolKind.BufferStop;
    }

    private static string FormatRouteName(RouteResult route)
    {
        var type = route.RouteType is RouteType.Shunting ? "Rangier" : "Zug";
        return $"{type}: {route.StartSignal.Name} -> {route.TargetSignal.Name}";
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
            TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal => "out",
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
            TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal => "in",
            _ when from.X <= to.X => "left",
            _ => "right"
        };

        return (fromPort, toPort);
    }

    private static IReadOnlyList<string> GetPortNames(DrawnTrackSymbol symbol)
    {
        return symbol.Kind switch
        {
            TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal => ["in", "out"],
            TrackSymbolKind.Switch => ["A", "B", "C"],
            TrackSymbolKind.DoubleSlipSwitch => ["A", "B", "C", "D"],
            TrackSymbolKind.Track or TrackSymbolKind.LineBlock => ["left", "right"],
            _ => ["left", "right"]
        };
    }

    private static Point GetPortPoint(DrawnTrackSymbol symbol, string portName)
    {
        if (symbol.Kind is TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal)
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
            TrackSymbolKind.ZwergSignal => GetThemeBrush("TrackPlanZwergSignalGreenBrush", Brushes.LightGreen),
            TrackSymbolKind.Switch => GetThemeBrush("TrackPlanSwitchBrush", Brushes.Blue),
            TrackSymbolKind.DoubleSlipSwitch => GetThemeBrush("TrackPlanDoubleSlipSwitchBrush", Brushes.LightSkyBlue),
            TrackSymbolKind.LineBlock => GetThemeBrush("TrackPlanBlockBrush", Brushes.Khaki),
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
            TrackSymbolKind.ZwergSignal => "ZW",
            TrackSymbolKind.Switch => "W",
            TrackSymbolKind.DoubleSlipSwitch => "DKW",
            TrackSymbolKind.LineBlock => "SB",
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
        if (symbol.Kind is TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal)
        {
            return $"{symbol.Name} {GetSignalArrow(symbol.SignalDirection)}";
        }

        if (symbol.Kind is TrackSymbolKind.LineBlock &&
            symbol.Properties.TryGetValue(Domino67PropertyNames.LineBlockDirection, out var direction))
        {
            return direction switch
            {
                Domino67PropertyNames.LineBlockDirectionOutgoing => $"{symbol.Name} [AB]",
                Domino67PropertyNames.LineBlockDirectionIncoming => $"{symbol.Name} [AN]",
                _ => symbol.Name
            };
        }

        return symbol.Name;
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
        ZwergSignal,
        Track,
        LineBlock,
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
            TrackEditorTool.ZwergSignal => TrackSymbolKind.ZwergSignal,
            TrackEditorTool.Track => TrackSymbolKind.Track,
            TrackEditorTool.LineBlock => TrackSymbolKind.LineBlock,
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

    private enum ReleaseTrigger
    {
        Manual,
        OnOccupy,
        OnFreeAfterOccupy
    }

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

    private void LoadStations()
    {
        Directory.CreateDirectory(_stationsDirectory);
        var (entries, links) = ReadStationData();
        _boundaryLinks.Clear();
        _boundaryLinks.AddRange(links);
        if (entries.Count == 0)
        {
            entries.Add(new StationEntry("bahnhof-1", "Bahnhof 1"));
            WriteStationData(entries, _boundaryLinks);
        }

        _stations.Clear();
        foreach (var entry in entries)
        {
            _stations[entry.Id] = CreateStationContext(entry.Id, entry.Name);
        }
        foreach (var link in _boundaryLinks)
        {
            MarkBoundaryHeartbeat(link.FromStationId, link.FromLineBlockId, link.ToStationId, link.ToLineBlockId);
        }

        StationSelector.ItemsSource = entries.Select(static x => x.Name).ToList();
        RefreshBoundaryStationSelector();
        SwitchToStation(entries[0].Id);
    }

    private StationContext CreateStationContext(string stationId, string stationName)
    {
        var stationDirectory = IOPath.Combine(_stationsDirectory, stationId);
        Directory.CreateDirectory(stationDirectory);
        var planFile = IOPath.Combine(stationDirectory, "plan.xml");
        var routesFile = IOPath.Combine(stationDirectory, "Routes.xml");
        var editor = new TrackPlanEditorModel(LoadTrackPlanDocument(planFile));
        EnsureLineBlockTravelDirections(editor.Document);
        var graph = editor.ToGraph();
        List<RouteResult> routes;
        try
        {
            routes = _routeDocumentStore.Load(routesFile, graph).ToList();
        }
        catch
        {
            routes = [];
        }

        return new StationContext(
            stationId,
            stationName,
            planFile,
            routesFile,
            editor,
            new StationInterlockingRuntime(new Domino67InterlockingProfile()),
            routes);
    }

    private void SwitchToStation(string stationId)
    {
        if (_activeStationId is not null && _stations.TryGetValue(_activeStationId, out var current))
        {
            SaveTrackPlan();
            SaveRoutes();
            current.VisibleRoutes = _visibleRoutes;
        }

        if (!_stations.TryGetValue(stationId, out var next))
        {
            return;
        }

        _activeStationId = stationId;
        _planFilePath = next.PlanFilePath;
        _routesFilePath = next.RoutesFilePath;
        _trackPlanEditor = next.Editor;
        _interlockingRuntime = next.InterlockingRuntime;
        _visibleRoutes = next.VisibleRoutes;
        StationSelector.SelectedItem = next.Name;
        RefreshBoundaryStationSelector();
        UpdateBoundaryRemoteBlockSelector();

        _highlightedConnectionKeys.Clear();
        _activeRouteConnectionKeys.Clear();
        _releasedRouteConnectionKeys.Clear();
        _selectedOperationSymbol = null;
        _operationStartSymbol = null;
        _selectedOperationRoute = null;
        _connectionStart = null;
        _connectionStartPort = null;
        RebuildActiveRouteConnectionKeys();

        if (_trackPlanEditor.Document.Symbols.Count == 0)
        {
            LoadDemoTrackPlan();
            TrackPlanStatus.Text = $"Neuer Gleisplan fuer {next.Name} erstellt.";
        }
        else
        {
            TrackPlanStatus.Text = $"Bahnhof {next.Name} geladen.";
            RefreshRouteLists();
        }

        UpdateActiveRouteText();
        RenderTrackPlan();
    }

    private (List<StationEntry> Stations, List<BoundaryLinkEntry> Links) ReadStationData()
    {
        if (!File.Exists(_stationsConfigPath))
        {
            return ([], []);
        }

        try
        {
            var xml = XDocument.Load(_stationsConfigPath);
            var stations = xml.Root?.Elements("Station")
                .Select(station => new StationEntry(
                    (string?)station.Attribute("id") ?? string.Empty,
                    (string?)station.Attribute("name") ?? string.Empty))
                .Where(static station => !string.IsNullOrWhiteSpace(station.Id) && !string.IsNullOrWhiteSpace(station.Name))
                .ToList() ?? [];

            var links = xml.Root?.Element("BoundaryLinks")?.Elements("Link")
                .Select(link => new BoundaryLinkEntry(
                    (string?)link.Attribute("fromStationId") ?? string.Empty,
                    (string?)link.Attribute("fromLineBlockId") ?? string.Empty,
                    (string?)link.Attribute("toStationId") ?? string.Empty,
                    (string?)link.Attribute("toLineBlockId") ?? string.Empty))
                .Where(static link =>
                    !string.IsNullOrWhiteSpace(link.FromStationId) &&
                    !string.IsNullOrWhiteSpace(link.FromLineBlockId) &&
                    !string.IsNullOrWhiteSpace(link.ToStationId) &&
                    !string.IsNullOrWhiteSpace(link.ToLineBlockId))
                .ToList() ?? [];

            return (stations, links);
        }
        catch
        {
            return ([], []);
        }
    }

    private void WriteStationData(IReadOnlyList<StationEntry> stations, IReadOnlyList<BoundaryLinkEntry> links)
    {
        var xml = new XDocument(
            new XElement("Stations",
                stations.Select(station => new XElement("Station",
                    new XAttribute("id", station.Id),
                    new XAttribute("name", station.Name))),
                new XElement("BoundaryLinks",
                    links.Select(link => new XElement("Link",
                        new XAttribute("fromStationId", link.FromStationId),
                        new XAttribute("fromLineBlockId", link.FromLineBlockId),
                        new XAttribute("toStationId", link.ToStationId),
                        new XAttribute("toLineBlockId", link.ToLineBlockId))))));
        xml.Save(_stationsConfigPath);
    }

    private string CreateStationId(string name)
    {
        var normalized = new string(name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "bahnhof";
        }

        var candidate = normalized;
        var index = 2;
        while (_stations.ContainsKey(candidate))
        {
            candidate = $"{normalized}-{index}";
            index++;
        }

        return candidate;
    }

    private void RefreshStationSelector()
    {
        var stations = _stations.Values.OrderBy(static s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        StationSelector.ItemsSource = stations.Select(static station => station.Name).ToList();
        if (_activeStationId is not null && _stations.TryGetValue(_activeStationId, out var active))
        {
            StationSelector.SelectedItem = active.Name;
        }
    }

    private void AddStation_OnClick(object? sender, RoutedEventArgs e)
    {
        var number = _stations.Count + 1;
        var name = $"Bahnhof {number}";
        while (_stations.Values.Any(station => station.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            number++;
            name = $"Bahnhof {number}";
        }

        var id = CreateStationId(name);
        var context = CreateStationContext(id, name);
        _stations[id] = context;
        WriteStationData(_stations.Values
            .OrderBy(static station => station.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static station => new StationEntry(station.Id, station.Name))
            .ToList(), _boundaryLinks);
        RefreshStationSelector();
        RefreshBoundaryStationSelector();
        SwitchToStation(id);
    }

    private void RemoveStation_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_activeStationId is null || _stations.Count <= 1)
        {
            TrackPlanStatus.Text = "Mindestens ein Bahnhof muss vorhanden sein.";
            return;
        }

        var removeId = _activeStationId;
        if (!_stations.TryGetValue(removeId, out var removeStation))
        {
            return;
        }

        var next = _stations.Values.First(station => !station.Id.Equals(removeId, StringComparison.OrdinalIgnoreCase));
        _stations.Remove(removeId);
        _boundaryLinks.RemoveAll(link =>
            link.FromStationId.Equals(removeId, StringComparison.OrdinalIgnoreCase) ||
            link.ToStationId.Equals(removeId, StringComparison.OrdinalIgnoreCase));
        WriteStationData(_stations.Values
            .OrderBy(static station => station.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static station => new StationEntry(station.Id, station.Name))
            .ToList(), _boundaryLinks);

        var removeDirectory = IOPath.Combine(_stationsDirectory, removeId);
        if (Directory.Exists(removeDirectory))
        {
            Directory.Delete(removeDirectory, true);
        }

        RefreshStationSelector();
        RefreshBoundaryStationSelector();
        SwitchToStation(next.Id);
        TrackPlanStatus.Text = $"Bahnhof {removeStation.Name} geloescht.";
    }

    private void StationSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (StationSelector.SelectedItem is not string stationName)
        {
            return;
        }

        var station = _stations.Values.FirstOrDefault(value => value.Name.Equals(stationName, StringComparison.OrdinalIgnoreCase));
        if (station is null || station.Id.Equals(_activeStationId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SwitchToStation(station.Id);
    }

    private sealed record StationEntry(string Id, string Name);
    private sealed record BoundaryLinkEntry(string FromStationId, string FromLineBlockId, string ToStationId, string ToLineBlockId);

    private static void EnsureLineBlockTravelDirections(TrackPlanDocument document)
    {
        foreach (var symbol in document.Symbols.Where(static s => s.Kind is TrackSymbolKind.LineBlock))
        {
            if (!symbol.Properties.ContainsKey(Domino67PropertyNames.LineBlockTravelDirection))
            {
                symbol.Properties[Domino67PropertyNames.LineBlockTravelDirection] = symbol.SignalDirection.ToString();
            }
        }
    }

    private void RefreshBoundaryStationSelector()
    {
        if (_activeStationId is null)
        {
            return;
        }

        var stations = _stations.Values
            .Where(station => !station.Id.Equals(_activeStationId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static station => station.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        BoundaryRemoteStationSelector.ItemsSource = stations.Select(static station => station.Name).ToList();
        BoundaryRemoteStationSelector.SelectedIndex = stations.Count > 0 ? 0 : -1;
    }

    private void BoundaryRemoteStationSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateBoundaryRemoteBlockSelector();
    }

    private void UpdateBoundaryRemoteBlockSelector()
    {
        if (BoundaryRemoteStationSelector.SelectedItem is not string remoteName)
        {
            BoundaryRemoteBlockSelector.ItemsSource = null;
            return;
        }

        var remoteStation = _stations.Values.FirstOrDefault(station => station.Name.Equals(remoteName, StringComparison.OrdinalIgnoreCase));
        if (remoteStation is null)
        {
            BoundaryRemoteBlockSelector.ItemsSource = null;
            return;
        }

        var blocks = remoteStation.Editor.Document.Symbols
            .Where(symbol => symbol.Kind is TrackSymbolKind.LineBlock)
            .Select(symbol => $"{symbol.Name} ({symbol.Id})")
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        BoundaryRemoteBlockSelector.ItemsSource = blocks;
        BoundaryRemoteBlockSelector.SelectedIndex = blocks.Count > 0 ? 0 : -1;
    }

    private void LinkLineBlock_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_activeStationId is null)
        {
            return;
        }

        if (_selectedBoundarySourceLineBlock is null || _selectedBoundarySourceLineBlock.Kind is not TrackSymbolKind.LineBlock)
        {
            TrackPlanStatus.Text = "Waehle zuerst im Gleisplan einen lokalen Streckenblock aus.";
            return;
        }

        if (BoundaryRemoteStationSelector.SelectedItem is not string remoteStationName ||
            BoundaryRemoteBlockSelector.SelectedItem is not string remoteBlockSelection)
        {
            TrackPlanStatus.Text = "Waehle Zielbahnhof und Ziel-Streckenblock.";
            return;
        }

        var remoteStation = _stations.Values.FirstOrDefault(station => station.Name.Equals(remoteStationName, StringComparison.OrdinalIgnoreCase));
        if (remoteStation is null)
        {
            return;
        }

        var start = remoteBlockSelection.LastIndexOf('(');
        var end = remoteBlockSelection.LastIndexOf(')');
        if (start < 0 || end <= start + 1)
        {
            TrackPlanStatus.Text = "Ziel-Streckenblock ungueltig.";
            return;
        }

        var remoteBlockId = remoteBlockSelection[(start + 1)..end];
        var entry = new BoundaryLinkEntry(_activeStationId, _selectedBoundarySourceLineBlock.Id, remoteStation.Id, remoteBlockId);
        var reverse = new BoundaryLinkEntry(remoteStation.Id, remoteBlockId, _activeStationId, _selectedBoundarySourceLineBlock.Id);

        if (!_boundaryLinks.Any(link =>
                link.FromStationId.Equals(entry.FromStationId, StringComparison.OrdinalIgnoreCase) &&
                link.FromLineBlockId.Equals(entry.FromLineBlockId, StringComparison.OrdinalIgnoreCase) &&
                link.ToStationId.Equals(entry.ToStationId, StringComparison.OrdinalIgnoreCase) &&
                link.ToLineBlockId.Equals(entry.ToLineBlockId, StringComparison.OrdinalIgnoreCase)))
        {
            _boundaryLinks.Add(entry);
        }

        if (!_boundaryLinks.Any(link =>
                link.FromStationId.Equals(reverse.FromStationId, StringComparison.OrdinalIgnoreCase) &&
                link.FromLineBlockId.Equals(reverse.FromLineBlockId, StringComparison.OrdinalIgnoreCase) &&
                link.ToStationId.Equals(reverse.ToStationId, StringComparison.OrdinalIgnoreCase) &&
                link.ToLineBlockId.Equals(reverse.ToLineBlockId, StringComparison.OrdinalIgnoreCase)))
        {
            _boundaryLinks.Add(reverse);
        }
        MarkBoundaryHeartbeat(entry.FromStationId, entry.FromLineBlockId, entry.ToStationId, entry.ToLineBlockId);
        MarkBoundaryHeartbeat(reverse.FromStationId, reverse.FromLineBlockId, reverse.ToStationId, reverse.ToLineBlockId);

        WriteStationData(_stations.Values
            .OrderBy(static station => station.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static station => new StationEntry(station.Id, station.Name))
            .ToList(), _boundaryLinks);

        SyncBoundaryState(_selectedBoundarySourceLineBlock);
        TrackPlanStatus.Text = $"{_selectedBoundarySourceLineBlock.Name} mit {remoteBlockSelection} verbunden.";
    }

    private void SyncBoundaryState(DrawnTrackSymbol localLineBlock)
    {
        if (_activeStationId is null || localLineBlock.Kind is not TrackSymbolKind.LineBlock)
        {
            return;
        }

        var hasLocalDirection = localLineBlock.Properties.TryGetValue(Domino67PropertyNames.LineBlockDirection, out var direction);
        var localDirection = hasLocalDirection
            ? direction!
            : Domino67PropertyNames.LineBlockDirectionOutgoing;
        var isBlocked = Domino67PropertyHelper.IsEnabled(localLineBlock, Domino67PropertyNames.BlockBlocked) ||
                        _occupiedSymbolIds.Contains(localLineBlock.Id) ||
                        _activeRoutes.Any(route => route.Symbols.Any(symbol => symbol.Id.Equals(localLineBlock.Id, StringComparison.OrdinalIgnoreCase)));

        var links = _boundaryLinks.Where(link =>
            link.FromStationId.Equals(_activeStationId, StringComparison.OrdinalIgnoreCase) &&
            link.FromLineBlockId.Equals(localLineBlock.Id, StringComparison.OrdinalIgnoreCase));

        foreach (var link in links)
        {
            if (!_stations.TryGetValue(link.ToStationId, out var remoteStation))
            {
                continue;
            }

            var remoteSymbol = remoteStation.Editor.Document.Symbols
                .FirstOrDefault(symbol => symbol.Id.Equals(link.ToLineBlockId, StringComparison.OrdinalIgnoreCase));
            if (remoteSymbol is not null)
            {
                if (isBlocked || hasLocalDirection)
                {
                    remoteStation.InterlockingRuntime.ApplyRemoteLineBlockState(
                        link.ToLineBlockId,
                        localDirection,
                        isBlocked,
                        remoteStation.Editor.Document);
                }
                else
                {
                    remoteSymbol.Properties.Remove(Domino67PropertyNames.BlockBlocked);
                }

                if (isBlocked)
                {
                    remoteStation.InterlockingRuntime.OccupiedSymbolIds.Add(remoteSymbol.Id);
                }
                else
                {
                    remoteStation.InterlockingRuntime.OccupiedSymbolIds.Remove(remoteSymbol.Id);
                }
            }

            // Auch in der Gegenstation gespeicherte Fahrstrassen erneut pruefen,
            // sobald sich ein gekoppelter Grenzblock aendert.
            var remotelySetCount = remoteStation.InterlockingRuntime.TrySetStoredRoutes(
                remoteStation.Editor.Document,
                CanStoreRoute,
                static (_, _) => { });
            if (remotelySetCount > 0)
            {
                EnsureRemoteIncomingDisplayForStation(remoteStation);
            }

            _trackPlanDocumentStore.Save(remoteStation.PlanFilePath, remoteStation.Editor.Document);
            MarkBoundaryHeartbeat(link.FromStationId, link.FromLineBlockId, link.ToStationId, link.ToLineBlockId);
        }
    }

    private void TryResetLineBlockToGrundstellung(DrawnTrackSymbol symbol)
    {
        if (symbol.Kind is not TrackSymbolKind.LineBlock)
        {
            return;
        }

        var hasActiveRoute = _activeRoutes.Any(route =>
            route.Symbols.Any(routeSymbol => routeSymbol.Id.Equals(symbol.Id, StringComparison.OrdinalIgnoreCase)));
        var isOccupied = _occupiedSymbolIds.Contains(symbol.Id);
        if (hasActiveRoute || isOccupied)
        {
            return;
        }

        symbol.Properties.Remove(Domino67PropertyNames.LineBlockDirection);
        symbol.Properties.Remove(Domino67PropertyNames.BlockBlocked);
    }

    private void SyncBoundaryGrundstellung(DrawnTrackSymbol localLineBlock)
    {
        if (_activeStationId is null || localLineBlock.Kind is not TrackSymbolKind.LineBlock)
        {
            return;
        }

        var links = _boundaryLinks.Where(link =>
            link.FromStationId.Equals(_activeStationId, StringComparison.OrdinalIgnoreCase) &&
            link.FromLineBlockId.Equals(localLineBlock.Id, StringComparison.OrdinalIgnoreCase));

        foreach (var link in links)
        {
            if (!_stations.TryGetValue(link.ToStationId, out var remoteStation))
            {
                continue;
            }

            var remoteSymbol = remoteStation.Editor.Document.Symbols
                .FirstOrDefault(symbol => symbol.Id.Equals(link.ToLineBlockId, StringComparison.OrdinalIgnoreCase));
            if (remoteSymbol is null)
            {
                continue;
            }

            remoteSymbol.Properties.Remove(Domino67PropertyNames.LineBlockDirection);
            remoteSymbol.Properties.Remove(Domino67PropertyNames.BlockBlocked);
            remoteStation.InterlockingRuntime.OccupiedSymbolIds.Remove(remoteSymbol.Id);

            var remotelySetCount = remoteStation.InterlockingRuntime.TrySetStoredRoutes(
                remoteStation.Editor.Document,
                CanStoreRoute,
                static (_, _) => { });
            if (remotelySetCount > 0)
            {
                EnsureRemoteIncomingDisplayForStation(remoteStation);
            }

            _trackPlanDocumentStore.Save(remoteStation.PlanFilePath, remoteStation.Editor.Document);
            MarkBoundaryHeartbeat(link.FromStationId, link.FromLineBlockId, link.ToStationId, link.ToLineBlockId);
        }
    }

    private void SyncBoundaryGrundstellungForRoutes(IEnumerable<RouteResult> routes)
    {
        foreach (var lineBlockId in routes
                     .SelectMany(route => route.Symbols)
                     .Where(symbol => symbol.Kind is TrackSymbolKind.LineBlock)
                     .Select(symbol => symbol.Id)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var localSymbol = FindSymbol(lineBlockId);
            if (localSymbol is not null)
            {
                SyncBoundaryGrundstellung(localSymbol);
            }
        }
    }

    private void EnsureRemoteIncomingDisplayForRoute(RouteResult route)
    {
        if (_activeStationId is null)
        {
            return;
        }

        var localLineBlockIds = route.Symbols
            .Where(symbol => symbol.Kind is TrackSymbolKind.LineBlock)
            .Select(symbol => symbol.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (localLineBlockIds.Count == 0)
        {
            return;
        }

        foreach (var link in _boundaryLinks.Where(link =>
                     link.FromStationId.Equals(_activeStationId, StringComparison.OrdinalIgnoreCase) &&
                     localLineBlockIds.Contains(link.FromLineBlockId)))
        {
            if (!_stations.TryGetValue(link.ToStationId, out var remoteStation))
            {
                continue;
            }

            var remoteSymbol = remoteStation.Editor.Document.Symbols
                .FirstOrDefault(symbol => symbol.Id.Equals(link.ToLineBlockId, StringComparison.OrdinalIgnoreCase));
            if (remoteSymbol is null)
            {
                continue;
            }

            remoteSymbol.Properties[Domino67PropertyNames.LineBlockDirection] = Domino67PropertyNames.LineBlockDirectionIncoming;
            _trackPlanDocumentStore.Save(remoteStation.PlanFilePath, remoteStation.Editor.Document);
            MarkBoundaryHeartbeat(link.FromStationId, link.FromLineBlockId, link.ToStationId, link.ToLineBlockId);
        }
    }

    private void EnsureRemoteIncomingDisplayForStation(StationContext station)
    {
        var activeLineBlockIds = station.InterlockingRuntime.ActiveRoutes
            .SelectMany(route => route.Symbols)
            .Where(symbol => symbol.Kind is TrackSymbolKind.LineBlock)
            .Select(symbol => symbol.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (activeLineBlockIds.Count == 0)
        {
            return;
        }

        foreach (var link in _boundaryLinks.Where(link =>
                     link.FromStationId.Equals(station.Id, StringComparison.OrdinalIgnoreCase) &&
                     activeLineBlockIds.Contains(link.FromLineBlockId)))
        {
            if (!_stations.TryGetValue(link.ToStationId, out var remoteStation))
            {
                continue;
            }

            var remoteSymbol = remoteStation.Editor.Document.Symbols
                .FirstOrDefault(symbol => symbol.Id.Equals(link.ToLineBlockId, StringComparison.OrdinalIgnoreCase));
            if (remoteSymbol is null)
            {
                continue;
            }

            remoteSymbol.Properties[Domino67PropertyNames.LineBlockDirection] = Domino67PropertyNames.LineBlockDirectionIncoming;
            _trackPlanDocumentStore.Save(remoteStation.PlanFilePath, remoteStation.Editor.Document);
            MarkBoundaryHeartbeat(link.FromStationId, link.FromLineBlockId, link.ToStationId, link.ToLineBlockId);
        }
    }

    private void MarkBoundaryHeartbeat(string fromStationId, string fromLineBlockId, string toStationId, string toLineBlockId)
    {
        var now = DateTimeOffset.UtcNow;
        _boundaryHeartbeat[BuildBoundaryHeartbeatKey(fromStationId, fromLineBlockId, toStationId, toLineBlockId)] = now;
        _boundaryHeartbeat[BuildBoundaryHeartbeatKey(toStationId, toLineBlockId, fromStationId, fromLineBlockId)] = now;
    }

    private static string BuildBoundaryHeartbeatKey(string fromStationId, string fromLineBlockId, string toStationId, string toLineBlockId)
    {
        return $"{fromStationId}:{fromLineBlockId}->{toStationId}:{toLineBlockId}";
    }

    private void SetLineBlockToGrundstellung(DrawnTrackSymbol symbol)
    {
        if (symbol.Kind is not TrackSymbolKind.LineBlock)
        {
            return;
        }

        var hasActiveRoute = _activeRoutes.Any(route =>
            route.Symbols.Any(routeSymbol => routeSymbol.Id.Equals(symbol.Id, StringComparison.OrdinalIgnoreCase)));
        if (hasActiveRoute)
        {
            TrackPlanStatus.Text = $"{symbol.Name} hat eine aktive Fahrstrasse und kann nicht in Grundstellung.";
            return;
        }

        _occupiedSymbolIds.Remove(symbol.Id);
        _releaseOnFreeSymbolIds.Remove(symbol.Id);
        symbol.Properties.Remove(Domino67PropertyNames.LineBlockDirection);
        symbol.Properties.Remove(Domino67PropertyNames.BlockBlocked);
        SyncBoundaryGrundstellung(symbol);
        SyncBoundaryState(symbol);
        TrySetStoredRoutes();
        SaveTrackPlan();
        TrackPlanStatus.Text = $"{symbol.Name} in Grundstellung.";
        RenderTrackPlan();
    }

    private void SetLineBlockToIncoming(DrawnTrackSymbol symbol)
    {
        if (symbol.Kind is not TrackSymbolKind.LineBlock)
        {
            return;
        }

        var hasActiveRoute = _activeRoutes.Any(route =>
            route.Symbols.Any(routeSymbol => routeSymbol.Id.Equals(symbol.Id, StringComparison.OrdinalIgnoreCase)));
        if (hasActiveRoute)
        {
            TrackPlanStatus.Text = $"{symbol.Name} hat eine aktive Fahrstrasse und kann nicht auf AN gestellt werden.";
            return;
        }

        _occupiedSymbolIds.Remove(symbol.Id);
        _releaseOnFreeSymbolIds.Remove(symbol.Id);
        symbol.Properties[Domino67PropertyNames.LineBlockDirection] = Domino67PropertyNames.LineBlockDirectionIncoming;
        symbol.Properties.Remove(Domino67PropertyNames.BlockBlocked);
        SyncBoundaryState(symbol);
        TrySetStoredRoutes();
        SaveTrackPlan();
        TrackPlanStatus.Text = $"{symbol.Name} auf AN gestellt.";
        RenderTrackPlan();
    }

    private void RebuildActiveRouteConnectionKeys()
    {
        _activeRouteConnectionKeys.Clear();
        foreach (var route in _activeRoutes)
        {
            foreach (var connection in route.Connections)
            {
                _activeRouteConnectionKeys.Add(GetConnectionKey(connection.FromSymbolId, connection.ToSymbolId));
                _activeRouteConnectionKeys.Add(GetConnectionKey(connection.ToSymbolId, connection.FromSymbolId));
            }
        }
    }

    private bool CanSetRouteAcrossBoundaries(RouteResult route, out string message)
    {
        message = string.Empty;
        if (_activeStationId is null)
        {
            return true;
        }

        var localLineBlockIds = route.Symbols
            .Where(symbol => symbol.Kind is TrackSymbolKind.LineBlock)
            .Select(symbol => symbol.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var link in _boundaryLinks.Where(link =>
                     link.FromStationId.Equals(_activeStationId, StringComparison.OrdinalIgnoreCase) &&
                     localLineBlockIds.Contains(link.FromLineBlockId)))
        {
            if (!_stations.TryGetValue(link.ToStationId, out var remoteStation))
            {
                message = $"Grenzblock-Kommunikation ungueltig: Zielstation {link.ToStationId} fehlt.";
                return false;
            }

            var remoteSymbol = remoteStation.Editor.Document.Symbols
                .FirstOrDefault(symbol => symbol.Id.Equals(link.ToLineBlockId, StringComparison.OrdinalIgnoreCase));
            if (remoteSymbol is null)
            {
                message = $"Grenzblock in {remoteStation.Name} nicht gefunden.";
                return false;
            }

            // Ein Zug auf der Strecke: Gegenblock darf weder belegt noch in aktiver Route sein.
            var remoteInActiveRoute = remoteStation.InterlockingRuntime.ActiveRoutes.Any(activeRoute =>
                activeRoute.Symbols.Any(symbol => symbol.Id.Equals(remoteSymbol.Id, StringComparison.OrdinalIgnoreCase)));
            var remoteOccupied = remoteStation.InterlockingRuntime.OccupiedSymbolIds.Contains(remoteSymbol.Id) ||
                                 Domino67PropertyHelper.IsEnabled(remoteSymbol, Domino67PropertyNames.BlockBlocked);
            if (remoteInActiveRoute || remoteOccupied)
            {
                message = $"Strecke belegt/gesperrt: {remoteStation.Name} - {remoteSymbol.Name}.";
                return false;
            }
        }

        return true;
    }

    private void SyncBoundaryStateForRoute(RouteResult route)
    {
        foreach (var localLineBlock in route.Symbols.Where(symbol => symbol.Kind is TrackSymbolKind.LineBlock))
        {
            var local = FindSymbol(localLineBlock.Id);
            if (local is not null)
            {
                SyncBoundaryState(local);
            }
        }
    }

    private void SyncBoundaryStateForRoutes(IEnumerable<RouteResult> routes)
    {
        foreach (var route in routes)
        {
            SyncBoundaryStateForRoute(route);
        }
    }

    private sealed class StationContext(
        string id,
        string name,
        string planFilePath,
        string routesFilePath,
        TrackPlanEditorModel editor,
        StationInterlockingRuntime interlockingRuntime,
        List<RouteResult> visibleRoutes)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public string PlanFilePath { get; } = planFilePath;
        public string RoutesFilePath { get; } = routesFilePath;
        public TrackPlanEditorModel Editor { get; } = editor;
        public StationInterlockingRuntime InterlockingRuntime { get; } = interlockingRuntime;
        public List<RouteResult> VisibleRoutes { get; set; } = visibleRoutes;
    }
}
