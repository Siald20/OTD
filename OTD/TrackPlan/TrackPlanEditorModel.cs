using System;
using System.Collections.Generic;
using System.Linq;

namespace OTD.TrackPlan;

public sealed class TrackPlanEditorModel
{
    private readonly TrackPlanGraphFactory _graphFactory = new();

    public TrackPlanEditorModel(TrackPlanDocument? document = null)
    {
        Document = document ?? new TrackPlanDocument();
    }

    public TrackPlanDocument Document { get; }

    public DrawnTrackSymbol AddSymbol(
        TrackSymbolKind kind,
        int x,
        int y,
        string? name = null)
    {
        var symbol = new DrawnTrackSymbol
        {
            Id = CreateId(kind),
            Kind = kind,
            Name = name ?? CreateDefaultName(kind),
            X = x,
            Y = y
        };

        if (kind is TrackSymbolKind.Switch)
        {
            symbol.SwitchRouteOptions.AddRange(CreateDefaultSwitchRouteOptions());
        }
        else if (kind is TrackSymbolKind.DoubleSlipSwitch)
        {
            symbol.CurrentSwitchPosition = SwitchPosition.Left;
            symbol.SwitchRouteOptions.AddRange(CreateDefaultDoubleSlipSwitchRouteOptions());
        }

        Document.Symbols.Add(symbol);
        return symbol;
    }

    public DrawnTrackConnection Connect(
        string fromSymbolId,
        string fromPort,
        string toSymbolId,
        string toPort,
        bool isBidirectional = true)
    {
        var connection = new DrawnTrackConnection
        {
            FromSymbolId = fromSymbolId,
            FromPort = fromPort,
            ToSymbolId = toSymbolId,
            ToPort = toPort,
            IsBidirectional = isBidirectional
        };

        Document.Connections.Add(connection);
        return connection;
    }

    public TrackPlanGraph ToGraph()
    {
        return _graphFactory.CreateGraph(Document);
    }

    private string CreateId(TrackSymbolKind kind)
    {
        var prefix = kind switch
        {
            TrackSymbolKind.Signal => "S",
            TrackSymbolKind.Switch => "W",
            TrackSymbolKind.DoubleSlipSwitch => "DKW",
            TrackSymbolKind.TrackBlock => "B",
            TrackSymbolKind.Sensor => "M",
            TrackSymbolKind.Platform => "P",
            TrackSymbolKind.LevelCrossing => "BU",
            TrackSymbolKind.TunnelPortal => "T",
            TrackSymbolKind.Bridge => "BR",
            TrackSymbolKind.Uncoupler => "E",
            TrackSymbolKind.Depot => "BW",
            TrackSymbolKind.Turntable => "DS",
            TrackSymbolKind.BufferStop => "PR",
            TrackSymbolKind.TextLabel => "TXT",
            _ => "G"
        };

        var index = Document.Symbols.Count(symbol => symbol.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) + 1;
        return $"{prefix}{index}";
    }

    private string CreateDefaultName(TrackSymbolKind kind)
    {
        return kind switch
        {
            TrackSymbolKind.Signal => "Signal",
            TrackSymbolKind.Switch => "Weiche",
            TrackSymbolKind.DoubleSlipSwitch => "Kreuzungsweiche",
            TrackSymbolKind.TrackBlock => "Block",
            TrackSymbolKind.Sensor => "Melder",
            TrackSymbolKind.Platform => "Bahnsteig",
            TrackSymbolKind.LevelCrossing => "Bahnuebergang",
            TrackSymbolKind.TunnelPortal => "Tunnel",
            TrackSymbolKind.Bridge => "Bruecke",
            TrackSymbolKind.Uncoupler => "Entkuppler",
            TrackSymbolKind.Depot => "Betriebswerk",
            TrackSymbolKind.Turntable => "Drehscheibe",
            TrackSymbolKind.BufferStop => "Prellbock",
            TrackSymbolKind.TextLabel => "Text",
            _ => "Gleis"
        };
    }

    private static IEnumerable<SwitchRouteOption> CreateDefaultSwitchRouteOptions()
    {
        yield return new SwitchRouteOption { FromPort = "A", ToPort = "B", Position = SwitchPosition.Straight };
        yield return new SwitchRouteOption { FromPort = "B", ToPort = "A", Position = SwitchPosition.Straight };
        yield return new SwitchRouteOption { FromPort = "A", ToPort = "C", Position = SwitchPosition.Diverging };
        yield return new SwitchRouteOption { FromPort = "C", ToPort = "A", Position = SwitchPosition.Diverging };
    }

    private static IEnumerable<SwitchRouteOption> CreateDefaultDoubleSlipSwitchRouteOptions()
    {
        yield return new SwitchRouteOption { FromPort = "A", ToPort = "B", Position = SwitchPosition.Straight };
        yield return new SwitchRouteOption { FromPort = "B", ToPort = "A", Position = SwitchPosition.Straight };
        yield return new SwitchRouteOption { FromPort = "C", ToPort = "D", Position = SwitchPosition.Diverging };
        yield return new SwitchRouteOption { FromPort = "D", ToPort = "C", Position = SwitchPosition.Diverging };
        yield return new SwitchRouteOption { FromPort = "A", ToPort = "D", Position = SwitchPosition.Right };
        yield return new SwitchRouteOption { FromPort = "D", ToPort = "A", Position = SwitchPosition.Right };
        yield return new SwitchRouteOption { FromPort = "C", ToPort = "B", Position = SwitchPosition.Left };
        yield return new SwitchRouteOption { FromPort = "B", ToPort = "C", Position = SwitchPosition.Left };
    }
}
