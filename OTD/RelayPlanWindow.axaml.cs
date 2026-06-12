using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace OTD;

public partial class RelayPlanWindow : Window
{
    private const double NodeWidth = 220;
    private const double HeaderHeight = 43;
    private const double PortHeight = 34;
    private readonly Do67 _do67 = new();
    private readonly List<RelayNode> _nodes = [];
    private readonly List<Cable> _cables = [];
    private Canvas _canvas = null!;
    private TextBlock _status = null!;
    private StackPanel _inspector = null!;
    private Port? _selectedPort;
    private object? _inspectedObject;
    private int _nodeCounter;
    private int _cycles;

    public RelayPlanWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _canvas = Find<Canvas>("PlanCanvas");
        _status = Find<TextBlock>("StatusText");
        _inspector = Find<StackPanel>("InspectorPanel");
        CreateExamplePlan();
    }

    private T Find<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException($"Control '{name}' fehlt.");

    private void CreateExamplePlan()
    {
        EnableTestPermissions();
        var wsr = AddNode("WSR 1", new TMN500_WSR(_do67, false), 80, 100);
        var grs = AddNode("GRS 1", new TMN817_GRS(_do67), 410, 100);
        var hsr = AddNode("HSR 1", new TMN501_HSR(_do67, false, false, false), 740, 100);
        Connect(FindPort(wsr, "sk_R"), FindPort(grs, "sk_L"));
        Connect(FindPort(grs, "sk_R"), FindPort(hsr, "sk_A"));
        Redraw();
    }

    private void EnableTestPermissions()
    {
        _do67.sl_TR_W.Value = true;
        _do67.sl_TR_HS.Value = true;
        _do67.sl_TR_ZS.Value = true;
        _do67.sl_TR_RF.Value = true;
        _do67.sl_TR_ZF.Value = true;
        _do67.sl_ST_GT.Value = true;
        _do67.sl_WIU_GT.Value = true;
        _do67.sl_AM_GT.Value = true;
        _do67.sl_ML.Value = true;
        _do67.sl_BLI.Value = true;
    }

    private RelayNode AddNode(string name, RelaisSatz relaySet, double x, double y)
    {
        var node = new RelayNode(name, relaySet, x, y);
        foreach (var field in relaySet.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)
                     .Where(field => field.FieldType == typeof(SpurStecker)))
            node.Ports.Add(new Port(node, field.Name, (SpurStecker)field.GetValue(relaySet)!));
        _nodes.Add(node);
        return node;
    }

    private void AddNew(string prefix, RelaisSatz relaySet)
    {
        _nodeCounter++;
        var column = _nodes.Count % 5;
        var row = _nodes.Count / 5;
        AddNode($"{prefix} {_nodeCounter}", relaySet, 80 + column * 330, 100 + row * 330);
        Redraw();
        SetStatus($"{prefix} hinzugefügt. Zum Verbinden zwei Stecker anklicken.");
    }

    private static Port FindPort(RelayNode node, string name) => node.Ports.Single(port => port.Name == name);

    private void Connect(Port first, Port second)
    {
        RemoveCableFor(first);
        RemoveCableFor(second);
        SpurStecker.Connect(first.Connector, second.Connector);
        _cables.Add(new Cable(first, second));
        RunSimulationSteps(2);
    }

    private void RemoveCableFor(Port port)
    {
        var cable = _cables.FirstOrDefault(item => item.First == port || item.Second == port);
        if (cable == null) return;
        SpurStecker.Disconnect(port.Connector);
        _cables.Remove(cable);
        RunSimulationSteps(2);
    }

    private void PortClicked(Port port)
    {
        if (_selectedPort == null)
        {
            _selectedPort = port;
            SetStatus($"{port.Node.Name}.{port.Name} gewählt. Jetzt Zielstecker anklicken.");
        }
        else if (_selectedPort == port)
        {
            _selectedPort = null;
            SetStatus("Auswahl aufgehoben.");
        }
        else
        {
            var first = _selectedPort;
            _selectedPort = null;
            Connect(first, port);
            SetStatus($"{first.Node.Name}.{first.Name} mit {port.Node.Name}.{port.Name} verbunden.");
        }
        Redraw();
    }

    private void Redraw()
    {
        _canvas.Children.Clear();
        foreach (var cable in _cables)
        {
            _canvas.Children.Add(new Line
            {
                StartPoint = GetPortPoint(cable.First),
                EndPoint = GetPortPoint(cable.Second),
                Stroke = Brushes.DeepSkyBlue,
                StrokeThickness = 5
            });
        }

        foreach (var node in _nodes)
        {
            var panel = new StackPanel();
            var headerButton = new Button
            {
                Content = $"{node.Name} · {node.RelaySet.GetType().Name}",
                Foreground = Brushes.White, FontWeight = FontWeight.SemiBold,
                Background = Brushes.Transparent, BorderThickness = new Avalonia.Thickness(0),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left
            };
            headerButton.Click += (_, _) => ShowInspector(node.Name, node.RelaySet);
            panel.Children.Add(new Border
            {
                Height = HeaderHeight, Background = new SolidColorBrush(Color.Parse("#24394A")),
                Padding = new Avalonia.Thickness(12, 9),
                Child = headerButton
            });
            foreach (var port in node.Ports)
            {
                var button = new Button
                {
                    Height = PortHeight,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    Content = $"{port.Name}   {(port.Connector.IsConnected ? "● verbunden" : "○ frei")}",
                    Background = new SolidColorBrush(Color.Parse(port == _selectedPort ? "#D88918" : "#18242E")),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.Parse(port.Connector.IsConnected ? "#2AA9E0" : "#526675")),
                    BorderThickness = new Avalonia.Thickness(1)
                };
                button.Click += (_, _) => PortClicked(port);
                panel.Children.Add(button);
            }

            var border = new Border
            {
                Width = NodeWidth, Background = new SolidColorBrush(Color.Parse("#101A22")),
                BorderBrush = new SolidColorBrush(Color.Parse("#587080")),
                BorderThickness = new Avalonia.Thickness(1), CornerRadius = new Avalonia.CornerRadius(5), Child = panel
            };
            Canvas.SetLeft(border, node.X);
            Canvas.SetTop(border, node.Y);
            _canvas.Children.Add(border);
        }
    }

    private static Avalonia.Point GetPortPoint(Port port)
    {
        var index = port.Node.Ports.IndexOf(port);
        return new Avalonia.Point(port.Node.X + NodeWidth, port.Node.Y + HeaderHeight + index * PortHeight + PortHeight / 2);
    }

    private void RunSimulation(object? sender, RoutedEventArgs e)
    {
        RunSimulationSteps(1);
    }

    private void OpenBlockTest(object? sender, RoutedEventArgs e)
    {
        new BlockTestWindow().Show(this);
    }

    private void RunTenSimulations(object? sender, RoutedEventArgs e)
    {
        RunSimulationSteps(10);
    }

    private void RunSimulationSteps(int count)
    {
        var rounds = 0;
        for (var step = 0; step < count; step++)
        {
            rounds = 0;
            while (rounds < 20)
            {
                foreach (var node in _nodes) node.RelaySet.Update();
                var changed = false;
                foreach (var node in _nodes) changed |= node.RelaySet.UpdateWire();
                rounds++;
                if (!changed) break;
            }
            foreach (var node in _nodes) UpdateComponents(node.RelaySet);
            foreach (var node in _nodes) node.RelaySet.Output();
            _cycles++;
        }
        Redraw();
        RefreshInspector();
        SetStatus($"Zyklus {_cycles} ausgeführt; Verdrahtung nach {rounds} Durchläufen stabil.");
    }

    private static void UpdateComponents(RelaisSatz relaySet)
    {
        foreach (var field in relaySet.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            switch (field.GetValue(relaySet))
            {
                case Relais relay: relay.Commit(); break;
                case RelaisInv inverseRelay: inverseRelay.Commit(); break;
                case Flachrelais flatRelay: flatRelay.Commit(); break;
                case Kondensator capacitor: capacitor.Update(); break;
            }
        }
    }

    private void DisconnectSelected(object? sender, RoutedEventArgs e)
    {
        if (_selectedPort == null) { SetStatus("Zuerst einen verbundenen Stecker auswählen."); return; }
        RemoveCableFor(_selectedPort);
        _selectedPort = null;
        Redraw();
        SetStatus("Spurkabel getrennt.");
    }

    private void ClearPlan(object? sender, RoutedEventArgs e)
    {
        foreach (var cable in _cables.ToArray()) SpurStecker.Disconnect(cable.First.Connector);
        _cables.Clear();
        _nodes.Clear();
        _selectedPort = null;
        Redraw();
        SetStatus("Plan geleert.");
    }

    private void AddGrs(object? sender, RoutedEventArgs e) => AddNew("GRS", new TMN817_GRS(_do67));
    private void AddWsr(object? sender, RoutedEventArgs e) => AddNew("WSR", new TMN500_WSR(_do67, false));
    private void AddHsr(object? sender, RoutedEventArgs e) => AddNew("HSR", new TMN501_HSR(_do67, false, false, false));
    private void AddZgr(object? sender, RoutedEventArgs e) => AddNew("ZGR", new TMN502_ZGR(_do67, false));
    private void AddZsr(object? sender, RoutedEventArgs e) => AddNew("ZSR", new TMN503_ZSR(_do67, false));
    private void AddVsr(object? sender, RoutedEventArgs e) => AddNew("VSR", new TMN814_VSR());
    private void AddTestSource(object? sender, RoutedEventArgs e) => AddNew("Prüfkabel", new SpurTestQuelle());
    private void SelectCentralControl(object? sender, RoutedEventArgs e) => ShowInspector("DO67 Zentrale", _do67);
    private void SetStatus(string text) => _status.Text = text;

    private void ShowInspector(string title, object target)
    {
        _inspectedObject = target;
        _inspector.Children.Clear();
        _inspector.Children.Add(Heading(title, 20));
        _inspector.Children.Add(new TextBlock
        {
            Text = target.GetType().Name,
            Foreground = new SolidColorBrush(Color.Parse("#9CB0BE"))
        });

        var fields = target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(field => field.Name)
            .ToArray();
        AddEditableSection("Tasten", fields, target, typeof(Taste));
        AddEditableSection("Eingänge", fields, target, typeof(Input), typeof(Output), typeof(Verbindung));
        AddRelaySection(fields, target);
        AddDiagnosticProperties(target);
        AddStateSection("Lampen", fields, target, typeof(Lampe));
        AddStateSection("Ausgänge", fields, target, typeof(Output));

        if (target is SpurTestQuelle source) AddTestPins(source);
        foreach (var field in fields.Where(field => field.FieldType == typeof(SpurStecker)))
            AddTrackPins(field.Name, (SpurStecker)field.GetValue(target)!);
    }

    private void AddEditableSection(string title, FieldInfo[] fields, object target, params Type[] types)
    {
        var matching = fields.Where(field => types.Contains(field.FieldType)).ToArray();
        if (matching.Length == 0) return;
        _inspector.Children.Add(Heading(title));
        foreach (var field in matching)
        {
            var value = field.GetValue(target)!;
            if (value is Taste taste) _inspector.Children.Add(CreateBistableButton(field.Name, taste));
            else _inspector.Children.Add(CreateBooleanCheckBox(field.Name, value));
        }
    }

    private Control CreateBistableButton(string name, Taste taste)
    {
        var checkBox = new CheckBox
        {
            Content = name,
            IsChecked = taste.Value,
            Foreground = Brushes.White
        };
        checkBox.IsCheckedChanged += (_, _) =>
        {
            taste.Value = checkBox.IsChecked == true;
            RunSimulationSteps(2);
            SetStatus($"Taste {name} {(taste.Value ? "eingeschaltet" : "ausgeschaltet")}.");
        };
        return checkBox;
    }

    private CheckBox CreateBooleanCheckBox(string name, object value)
    {
        var property = value.GetType().GetProperty("Value")!;
        var checkBox = new CheckBox { Content = name, IsChecked = (bool)property.GetValue(value)! };
        checkBox.IsCheckedChanged += (_, _) =>
        {
            property.SetValue(value, checkBox.IsChecked == true);
            RunSimulationSteps(2);
            SetStatus($"{name} {(checkBox.IsChecked == true ? "eingeschaltet" : "ausgeschaltet")}.");
        };
        return checkBox;
    }

    private void AddRelaySection(FieldInfo[] fields, object target)
    {
        var matching = fields.Where(field => typeof(RelaisBase).IsAssignableFrom(field.FieldType)).ToArray();
        if (matching.Length == 0) return;
        _inspector.Children.Add(Heading("Relais"));
        foreach (var field in matching)
        {
            var relay = (RelaisBase)field.GetValue(target)!;
            _inspector.Children.Add(StateRow(field.Name, relay.Value, relay.IstAngezogen() ? "angezogen" : "abgefallen"));
        }
    }

    private void AddDiagnosticProperties(object target)
    {
        var properties = target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(bool) && property.CanRead && !property.CanWrite)
            .OrderBy(property => property.Name)
            .ToArray();
        if (properties.Length == 0) return;

        _inspector.Children.Add(Heading("Diagnose"));
        foreach (var property in properties)
        {
            var state = (bool)property.GetValue(target)!;
            _inspector.Children.Add(StateRow(property.Name, state, state ? "1" : "0"));
        }
    }

    private void AddStateSection(string title, FieldInfo[] fields, object target, Type fieldType)
    {
        var matching = fields.Where(field => field.FieldType == fieldType).ToArray();
        if (matching.Length == 0) return;
        _inspector.Children.Add(Heading(title));
        foreach (var field in matching)
        {
            var value = field.GetValue(target)!;
            var state = (bool)value.GetType().GetProperty("Value")!.GetValue(value)!;
            _inspector.Children.Add(StateRow(field.Name, state, state ? "EIN" : "aus"));
        }
    }

    private void AddTestPins(SpurTestQuelle source)
    {
        _inspector.Children.Add(Heading("Prüfkabel einspeisen"));
        for (var pin = 1; pin <= 24; pin++)
        {
            var currentPin = pin;
            var combo = new ComboBox { Width = 90, ItemsSource = new[] { "-1", "0", "+1" } };
            combo.SelectedIndex = source.GetValue(pin) + 1;
            combo.SelectionChanged += (_, _) =>
            {
                source.SetValue(currentPin, (sbyte)(combo.SelectedIndex - 1));
                RunSimulationSteps(2);
                SetStatus($"Prüfkabel Pin {currentPin} auf {combo.SelectedItem} gesetzt.");
            };
            _inspector.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock { Text = $"Pin {pin}", Foreground = Brushes.White, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                    combo
                }
            });
            Grid.SetColumn(combo, 1);
        }
    }

    private void AddTrackPins(string name, SpurStecker connector)
    {
        _inspector.Children.Add(Heading($"{name}: Eingang / Ausgang"));
        for (var pin = 1; pin <= 24; pin++)
            _inspector.Children.Add(StateRow($"Pin {pin:00}", connector.Get(pin) != 0,
                $"{FormatPin(connector.Get(pin))} / {FormatPin(connector.GetOutput(pin))}"));
    }

    private static string FormatPin(sbyte value) => value switch { -1 => "-", 1 => "+", _ => "0" };

    private static TextBlock Heading(string text, double size = 15) => new()
    {
        Text = text, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold,
        FontSize = size, Margin = new Avalonia.Thickness(0, 8, 0, 2)
    };

    private static Control StateRow(string name, bool active, string state) => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("14,*,Auto"),
        Children =
        {
            new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.Parse(active ? "#5BE06D" : "#40505C")) },
            new TextBlock { Text = name, Foreground = Brushes.White, Margin = new Avalonia.Thickness(7, 0, 0, 0) },
            new TextBlock { Text = state, Foreground = new SolidColorBrush(Color.Parse("#9CB0BE")) }
        }
    }.WithColumns();

    private void RefreshInspector()
    {
        if (_inspectedObject == null) return;
        var title = _nodes.FirstOrDefault(node => ReferenceEquals(node.RelaySet, _inspectedObject))?.Name ?? "DO67 Zentrale";
        ShowInspector(title, _inspectedObject);
    }

    private sealed record RelayNode(string Name, RelaisSatz RelaySet, double X, double Y) { public List<Port> Ports { get; } = []; }
    private sealed record Port(RelayNode Node, string Name, SpurStecker Connector);
    private sealed record Cable(Port First, Port Second);

}

internal static class GridColumnExtensions
{
    public static T WithColumns<T>(this T grid) where T : Grid
    {
        if (grid.Children.Count > 1) Grid.SetColumn(grid.Children[1], 1);
        if (grid.Children.Count > 2) Grid.SetColumn(grid.Children[2], 2);
        return grid;
    }
}
