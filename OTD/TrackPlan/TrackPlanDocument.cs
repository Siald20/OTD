using System.Collections.Generic;

namespace OTD.TrackPlan;

public sealed class TrackPlanDocument
{
    public string Name { get; set; } = "Neuer Gleisplan";

    public int Version { get; set; } = 1;

    public List<DrawnTrackSymbol> Symbols { get; set; } = [];

    public List<DrawnTrackConnection> Connections { get; set; } = [];
}

public sealed class DrawnTrackSymbol
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public TrackSymbolKind Kind { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public SignalDirection SignalDirection { get; set; } = SignalDirection.Both;

    public SwitchPosition CurrentSwitchPosition { get; set; } = SwitchPosition.Straight;

    public List<SwitchRouteOption> SwitchRouteOptions { get; set; } = [];

    public Dictionary<string, string> Properties { get; set; } = [];
}

public sealed class DrawnTrackConnection
{
    public string FromSymbolId { get; set; } = string.Empty;

    public string FromPort { get; set; } = string.Empty;

    public string ToSymbolId { get; set; } = string.Empty;

    public string ToPort { get; set; } = string.Empty;

    public int Cost { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public bool IsBidirectional { get; set; } = true;
}
