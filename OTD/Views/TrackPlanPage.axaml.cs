using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.VisualTree;
using OTD.Controlls.InterlockingEnlements;
using System;
using System.Collections.Generic;

namespace OTD.Views;

public partial class TrackPlanPage : UserControl
{
    private sealed class RingColorOption
    {
        public required string Name { get; init; }
        public required string Hex { get; init; }
        public override string ToString() => Name;
    }

    private sealed class SignalTileSettings
    {
        public string Name { get; set; } = "xxx";
        public bool MemoryBlinking { get; set; }
        public string RingColorHex { get; set; } = "#E6C200";
    }

    private sealed class PlacedSymbol
    {
        public required string SymbolKey { get; set; }
        public int RotationDegrees { get; set; }
        public SignalTileSettings? SignalSettings { get; set; }
    }

    private string _activeElement = "signal-memory";
    private readonly Dictionary<(int Row, int Column), PlacedSymbol> _placedSymbols = [];
    private int _gridRows = 12;
    private int _gridColumns = 20;
    private int _baseGridRows = 12;
    private int _baseGridColumns = 20;
    private int _cellSize = 120;
    private double _gridZoom = 1.0;
    private (int Row, int Column)? _selectedCell;

    public TrackPlanPage()
        : this(null)
    {
    }

    public TrackPlanPage(Action? navigateBack)
    {
        InitializeComponent();
        InitializeUi();
    }

    private void InitializeUi()
    {
        SymbolCategoryTabs.SelectedIndex = 0;
        BuildPalette("signaltiles");
        RebuildSymbolPlacementGrid();
        UpdateGridControlsText();
        ApplyGridZoom();
        TrackPlanStatus.Text = "Symbol auswaehlen und per Drag-and-Drop platzieren.";
    }

    private void BuildPalette(string category)
    {
        ElementPalette.Children.Clear();

        var items = category switch
        {
            "signaltiles" => new[]
            {
                new { Key = "signal-memory", Label = "Signal (Speicher)" },
                new { Key = "signal-nomemory", Label = "Signal (ohne Speicher)" },
                new { Key = "delete", Label = "Loeschen" }
            },
            _ => new[]
            {
                new { Key = "delete", Label = "Loeschen" }
            }
        };

        foreach (var item in items)
        {
            var button = new Button
            {
                Classes = { "tool" },
                Margin = new Thickness(4),
                Tag = item.Key,
                Width = 110,
                Height = 110
            };

            button.Content = CreatePalettePreview(item.Key, item.Label);

            button.Click += (_, _) =>
            {
                _activeElement = item.Key;
                EnsureCellSizeForSymbol(_activeElement);
                TrackPlanStatus.Text = item.Key == "delete"
                    ? "Werkzeug: Loeschen"
                    : $"Element gewaehlt: {item.Label}";
            };
            button.PointerPressed += PaletteButton_OnPointerPressed;
            ElementPalette.Children.Add(button);
        }
    }

    private void SymbolCategoryTabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SymbolCategoryTabs.SelectedItem is ListBoxItem { Tag: string category })
        {
            BuildPalette(category);
        }
    }

    private async void PaletteButton_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string key)
        {
            return;
        }

        _activeElement = key;
        EnsureCellSizeForSymbol(_activeElement);
        var data = new DataObject();
        data.Set("track-symbol", key);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
    }

    private void GridColumnsSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        _gridColumns = Math.Max(1, (int)Math.Round(e.NewValue));
        _baseGridColumns = _gridColumns;
        UpdateGridControlsText();
        RebuildSymbolPlacementGrid();
    }

    private void GridRowsSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        _gridRows = Math.Max(1, (int)Math.Round(e.NewValue));
        _baseGridRows = _gridRows;
        UpdateGridControlsText();
        RebuildSymbolPlacementGrid();
    }

    private void CellSizeSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        _cellSize = Math.Max(12, (int)Math.Round(e.NewValue));
        UpdateGridControlsText();
        RebuildSymbolPlacementGrid();
    }

    private void GridZoomSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        var previousZoom = _gridZoom;
        _gridZoom = Math.Clamp(e.NewValue / 100.0, 0.25, 4.0);
        EnsureGridCapacityForZoom(previousZoom, _gridZoom);
        UpdateGridControlsText();
        ApplyGridZoom();
    }

    private void RebuildSymbolPlacementGrid()
    {
        SymbolPlacementGrid.RowDefinitions.Clear();
        SymbolPlacementGrid.ColumnDefinitions.Clear();
        SymbolPlacementGrid.Children.Clear();

        for (var row = 0; row < _gridRows; row++)
        {
            SymbolPlacementGrid.RowDefinitions.Add(new RowDefinition(_cellSize, GridUnitType.Pixel));
        }

        for (var col = 0; col < _gridColumns; col++)
        {
            SymbolPlacementGrid.ColumnDefinitions.Add(new ColumnDefinition(_cellSize, GridUnitType.Pixel));
        }

        SymbolPlacementGrid.Width = _gridColumns * _cellSize;
        SymbolPlacementGrid.Height = _gridRows * _cellSize;

        for (var row = 0; row < _gridRows; row++)
        {
            for (var col = 0; col < _gridColumns; col++)
            {
                var cell = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#7E9F88")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#6E8E77")),
                    BorderThickness = new Thickness(1),
                    Tag = (row, col)
                };
                DragDrop.SetAllowDrop(cell, true);
                cell.PointerPressed += GridCell_OnPointerPressed;
                cell.AddHandler(DragDrop.DragOverEvent, GridCell_OnDragOver);
                cell.AddHandler(DragDrop.DropEvent, GridCell_OnDrop);
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, col);

                if (_placedSymbols.TryGetValue((row, col), out var symbol))
                {
                    cell.Child = CreateSymbolVisual(symbol);
                }

                SymbolPlacementGrid.Children.Add(cell);
            }
        }
    }

    private void GridCell_OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is not Border cell || cell.Tag is not ValueTuple<int, int> position)
        {
            return;
        }

        PlacementScrollViewer.Focus();
        _selectedCell = (position.Item1, position.Item2);

        if (e.GetCurrentPoint(cell).Properties.IsRightButtonPressed)
        {
            if (_placedSymbols.TryGetValue((position.Item1, position.Item2), out var placed) &&
                placed.SymbolKey is "signal-memory" or "signal-nomemory")
            {
                OpenSignalTileContextMenu(cell, position, placed);
            }
            else
            {
                _placedSymbols.Remove((position.Item1, position.Item2));
                cell.Child = null;
                TrackPlanStatus.Text = $"Zelle geloescht: Zeile {position.Item1 + 1}, Spalte {position.Item2 + 1}.";
            }
            return;
        }

        if (_activeElement == "delete")
        {
            _placedSymbols.Remove((position.Item1, position.Item2));
            cell.Child = null;
            TrackPlanStatus.Text = $"Zelle geloescht: Zeile {position.Item1 + 1}, Spalte {position.Item2 + 1}.";
            return;
        }

        if (_placedSymbols.ContainsKey((position.Item1, position.Item2)))
        {
            TrackPlanStatus.Text = $"Zelle bereits belegt: Zeile {position.Item1 + 1}, Spalte {position.Item2 + 1}.";
            return;
        }

        var placedSymbol = CreatePlacedSymbol(_activeElement);
        _placedSymbols[(position.Item1, position.Item2)] = placedSymbol;
        cell.Child = CreateSymbolVisual(placedSymbol);
        TrackPlanStatus.Text = $"{_activeElement} platziert: Zeile {position.Item1 + 1}, Spalte {position.Item2 + 1}.";
    }

    private void GridCell_OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains("track-symbol") ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void GridCell_OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Border cell || cell.Tag is not ValueTuple<int, int> position)
        {
            return;
        }

        if (e.Data.Get("track-symbol") is not string symbolLabel || string.IsNullOrWhiteSpace(symbolLabel))
        {
            return;
        }

        _activeElement = symbolLabel;
        EnsureCellSizeForSymbol(_activeElement);
        if (symbolLabel == "delete")
        {
            _placedSymbols.Remove((position.Item1, position.Item2));
            cell.Child = null;
            TrackPlanStatus.Text = $"Zelle geloescht: Zeile {position.Item1 + 1}, Spalte {position.Item2 + 1}.";
            e.Handled = true;
            return;
        }

        if (_placedSymbols.ContainsKey((position.Item1, position.Item2)))
        {
            TrackPlanStatus.Text = $"Zelle bereits belegt: Zeile {position.Item1 + 1}, Spalte {position.Item2 + 1}.";
            e.Handled = true;
            return;
        }

        var placedSymbol = CreatePlacedSymbol(symbolLabel);
        _placedSymbols[(position.Item1, position.Item2)] = placedSymbol;
        cell.Child = CreateSymbolVisual(placedSymbol);
        TrackPlanStatus.Text = $"{symbolLabel} per Drag&Drop platziert: Zeile {position.Item1 + 1}, Spalte {position.Item2 + 1}.";
        e.Handled = true;
    }

    private static Border CreateSymbolVisual(PlacedSymbol symbol)
    {
        var rotation = new RotateTransform(symbol.RotationDegrees);

        if (symbol.SymbolKey == "signal-memory")
        {
            var settings = symbol.SignalSettings ?? new SignalTileSettings();
            var ringBrush = new SolidColorBrush(Color.Parse(GetMemoryRingColorByRotation(symbol.RotationDegrees)));
            var control = new SignalTileMemory
            {
                Width = 120,
                Height = 120,
                SignalName = settings.Name,
                MemoryBlinking = settings.MemoryBlinking,
                MemoryRingBrush = ringBrush,
                IsHitTestVisible = false,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = rotation
            };
            return new Border
            {
                Margin = new Thickness(0),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 120,
                Height = 120,
                Child = new TrackTiles
                {
                    SymbolContent = control
                }
            };
        }

        if (symbol.SymbolKey == "signal-nomemory")
        {
            var settings = symbol.SignalSettings ?? new SignalTileSettings();
            var control = new SignalTileNoMemory
            {
                Width = 120,
                Height = 120,
                SignalName = settings.Name,
                IsHitTestVisible = false,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = rotation
            };
            return new Border
            {
                Margin = new Thickness(0),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 120,
                Height = 120,
                Child = new TrackTiles
                {
                    SymbolContent = control
                }
            };
        }

        return new Border
        {
            Margin = new Thickness(2),
            Background = Brushes.SlateGray,
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = new RotateTransform(symbol.RotationDegrees),
            Child = new TextBlock
            {
                Text = symbol.SymbolKey,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 11
            }
        };
    }

    private void UpdateGridControlsText()
    {
        GridColumnsText.Text = $"Spalten: {_gridColumns}";
        GridRowsText.Text = $"Zeilen: {_gridRows}";
        CellSizeText.Text = $"Zellgroesse: {_cellSize}";
        GridZoomText.Text = $"Zoom: {(int)Math.Round(_gridZoom * 100)}%";
    }

    private void ApplyGridZoom()
    {
        PlacementZoomHost.RenderTransform = new ScaleTransform(_gridZoom, _gridZoom);
    }

    private void PlacementScrollViewer_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        var step = e.Delta.Y > 0 ? 10 : -10;
        var newZoomPercent = Math.Clamp(GridZoomSlider.Value + step, GridZoomSlider.Minimum, GridZoomSlider.Maximum);
        GridZoomSlider.Value = newZoomPercent;
        e.Handled = true;
    }

    private void PlacementScrollViewer_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.R || _selectedCell is null)
        {
            return;
        }

        var position = _selectedCell.Value;
        if (!_placedSymbols.TryGetValue(position, out var placedSymbol))
        {
            return;
        }

        placedSymbol.RotationDegrees = (placedSymbol.RotationDegrees + 90) % 360;
        _placedSymbols[position] = placedSymbol;
        RebuildSymbolPlacementGrid();
        TrackPlanStatus.Text = $"Rotation: {placedSymbol.RotationDegrees} Grad (Zeile {position.Row + 1}, Spalte {position.Column + 1}).";
        e.Handled = true;
    }

    private void EnsureCellSizeForSymbol(string symbolKey)
    {
        var preferred = GetPreferredCellSize(symbolKey);
        if (preferred <= _cellSize)
        {
            return;
        }

        _cellSize = preferred;
        CellSizeSlider.Value = preferred;
        UpdateGridControlsText();
        RebuildSymbolPlacementGrid();
    }

    private static int GetPreferredCellSize(string symbolKey)
    {
        return symbolKey switch
        {
            "signal-memory" => 120,
            "signal-nomemory" => 120,
            _ => 36
        };
    }

    private static PlacedSymbol CreatePlacedSymbol(string symbolKey)
    {
        return symbolKey is "signal-memory" or "signal-nomemory"
            ? new PlacedSymbol
            {
                SymbolKey = symbolKey,
                RotationDegrees = 0,
                SignalSettings = new SignalTileSettings()
            }
            : new PlacedSymbol { SymbolKey = symbolKey, RotationDegrees = 0 };
    }

    private static Control CreatePalettePreview(string symbolKey, string label)
    {
        if (symbolKey == "signal-memory")
        {
            return new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new Border
                    {
                        Height = 82,
                        Child = new Viewbox
                        {
                            Stretch = Stretch.Uniform,
                            Child = new SignalTileMemory { IsHitTestVisible = false }
                        }
                    },
                    new TextBlock
                    {
                        Text = label,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        FontSize = 11
                    }
                }
            };
        }

        if (symbolKey == "signal-nomemory")
        {
            return new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new Border
                    {
                        Height = 82,
                        Child = new Viewbox
                        {
                            Stretch = Stretch.Uniform,
                            Child = new SignalTileNoMemory { IsHitTestVisible = false }
                        }
                    },
                    new TextBlock
                    {
                        Text = label,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        FontSize = 11
                    }
                }
            };
        }

        return new StackPanel
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "X",
                    FontSize = 22,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = label,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 11
                }
            }
        };
    }

    private void EnsureGridCapacityForZoom(double previousZoom, double newZoom)
    {
        if (newZoom >= previousZoom)
        {
            return;
        }

        var targetColumns = (int)Math.Ceiling(_baseGridColumns / newZoom);
        var targetRows = (int)Math.Ceiling(_baseGridRows / newZoom);

        var changed = false;
        if (targetColumns > _gridColumns)
        {
            _gridColumns = targetColumns;
            changed = true;
        }

        if (targetRows > _gridRows)
        {
            _gridRows = targetRows;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        if (_gridColumns > GridColumnsSlider.Maximum)
        {
            GridColumnsSlider.Maximum = _gridColumns;
        }

        if (_gridRows > GridRowsSlider.Maximum)
        {
            GridRowsSlider.Maximum = _gridRows;
        }

        GridColumnsSlider.Value = _gridColumns;
        GridRowsSlider.Value = _gridRows;
        RebuildSymbolPlacementGrid();
    }

    private void OpenSignalTileContextMenu(Border cell, (int Row, int Column) position, PlacedSymbol placedSymbol)
    {
        var settingsItem = new MenuItem { Header = "Einstellungen" };
        settingsItem.Click += (_, _) => OpenSignalSettingsDialog(cell, position, placedSymbol);

        var deleteItem = new MenuItem { Header = "Loeschen" };
        deleteItem.Click += (_, _) =>
        {
            _placedSymbols.Remove((position.Row, position.Column));
            cell.Child = null;
            TrackPlanStatus.Text = $"Zelle geloescht: Zeile {position.Row + 1}, Spalte {position.Column + 1}.";
        };

        var menu = new ContextMenu
        {
            Items =
            {
                settingsItem,
                new Separator(),
                deleteItem
            }
        };

        menu.Open(cell);
    }

    private async void OpenSignalSettingsDialog(Border cell, (int Row, int Column) position, PlacedSymbol placedSymbol)
    {
        var settings = placedSymbol.SignalSettings ?? new SignalTileSettings();
        var nameBox = new TextBox { Text = settings.Name };
        var rotationOptions = new[]
        {
            new RingColorOption { Name = "0 Grad", Hex = "0" },
            new RingColorOption { Name = "90 Grad", Hex = "90" },
            new RingColorOption { Name = "180 Grad", Hex = "180" },
            new RingColorOption { Name = "270 Grad", Hex = "270" }
        };
        var rotationCombo = new ComboBox { ItemsSource = rotationOptions };
        var rotationIndex = Array.FindIndex(rotationOptions, x => x.Hex == placedSymbol.RotationDegrees.ToString());
        rotationCombo.SelectedIndex = rotationIndex >= 0 ? rotationIndex : 0;
        var typeOptions = new[]
        {
            new RingColorOption { Name = "Signal mit Speicher", Hex = "signal-memory" },
            new RingColorOption { Name = "Signal ohne Speicher", Hex = "signal-nomemory" }
        };
        var typeCombo = new ComboBox { ItemsSource = typeOptions };
        typeCombo.SelectedIndex = placedSymbol.SymbolKey == "signal-nomemory" ? 1 : 0;
        var memoryBlinkingBox = new CheckBox { Content = "Speicher blinkt", IsChecked = settings.MemoryBlinking };

        var tabs = new TabControl
        {
            Items =
            {
                new TabItem
                {
                    Header = "Allgemein",
                    Content = new StackPanel
                    {
                        Margin = new Thickness(10),
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = "Name" },
                            nameBox,
                            new TextBlock { Text = "Symboltyp" },
                            typeCombo,
                            new TextBlock { Text = "Drehung" },
                            rotationCombo,
                            memoryBlinkingBox
                        }
                    }
                },
                new TabItem
                {
                    Header = "Schnittstelle",
                    Content = new StackPanel
                    {
                        Margin = new Thickness(10),
                        Children =
                        {
                            new TextBlock { Text = "Noch ohne Konfiguration." }
                        }
                    }
                },
                new TabItem
                {
                    Header = "Darstellung",
                    Content = new StackPanel
                    {
                        Margin = new Thickness(10),
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = "Ringfarbe (Signal mit Speicher):" },
                            new TextBlock { Text = "0/90 Grad = Hellblau, 180/270 Grad = Hellgrau" }
                        }
                    }
                }
            }
        };

        var saveButton = new Button { Content = "Speichern", Classes = { "primary" }, MinWidth = 100 };
        var cancelButton = new Button { Content = "Abbrechen", MinWidth = 100 };
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, saveButton }
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { tabs, buttonRow }
        };
        Grid.SetRow(buttonRow, 1);

        var dialog = new Window
        {
            Width = 520,
            Height = 380,
            Title = "SignalTiles Einstellungen",
            Content = root
        };

        saveButton.Click += (_, _) => dialog.Close(true);
        cancelButton.Click += (_, _) => dialog.Close(false);

        var owner = this.GetVisualRoot() as Window;
        if (owner is null)
        {
            return;
        }

        var accepted = await dialog.ShowDialog<bool>(owner);

        if (!accepted)
        {
            return;
        }

        settings.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? "xxx" : nameBox.Text.Trim();
        var selectedType = (typeCombo.SelectedItem as RingColorOption)?.Hex ?? "signal-memory";
        placedSymbol.SymbolKey = selectedType;
        var selectedRotation = (rotationCombo.SelectedItem as RingColorOption)?.Hex ?? "0";
        placedSymbol.RotationDegrees = int.TryParse(selectedRotation, out var rot) ? rot : 0;
        settings.MemoryBlinking = (memoryBlinkingBox.IsChecked ?? false) && placedSymbol.SymbolKey == "signal-memory";

        placedSymbol.SignalSettings = settings;
        cell.Child = CreateSymbolVisual(placedSymbol);
        _placedSymbols[(position.Row, position.Column)] = placedSymbol;
        TrackPlanStatus.Text = $"Signal aktualisiert: {settings.Name}";
    }

    private static string GetMemoryRingColorByRotation(int rotationDegrees)
    {
        var normalized = ((rotationDegrees % 360) + 360) % 360;
        return normalized is 0 or 90 ? "#A6D8FF" : "#D3D7DC";
    }

}
