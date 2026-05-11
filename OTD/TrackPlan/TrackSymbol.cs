using System;
using System.Collections.Generic;

namespace OTD.TrackPlan;

public sealed class TrackSymbol
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public TrackSymbolKind Kind { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public SignalDirection SignalDirection { get; init; } = SignalDirection.Both;

    public bool IsOccupied { get; set; }

    public bool IsLocked { get; set; }

    public SwitchPosition CurrentSwitchPosition { get; set; } = SwitchPosition.Straight;

    public List<SwitchRouteOption> SwitchRouteOptions { get; init; } = [];

    public Dictionary<string, string> Properties { get; init; } = [];

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Name) ? Id : $"{Name} ({Id})";
    }
}

public sealed class SwitchRouteOption
{
    public string FromPort { get; init; } = string.Empty;

    public string ToPort { get; init; } = string.Empty;

    public SwitchPosition Position { get; init; }

    public bool Matches(string fromPort, string toPort)
    {
        return string.Equals(FromPort, fromPort, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(ToPort, toPort, StringComparison.OrdinalIgnoreCase);
    }
}
