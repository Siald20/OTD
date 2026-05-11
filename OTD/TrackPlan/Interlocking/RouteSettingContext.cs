using System;
using System.Collections.Generic;
using System.Linq;

namespace OTD.TrackPlan.Interlocking;

/// <summary>
/// Gemeinsamer Kontext fuer alle Elementlogiken waehrend eines Stellversuchs.
/// Er kapselt Route, Dokumentzustand und Hilfszugriffe, damit die einzelnen
/// Elementklassen nicht selbst in Listen suchen oder UI-Zustand kennen muessen.
/// </summary>
public sealed class RouteSettingContext
{
    private readonly Dictionary<string, SwitchCommand> _switchCommandsById;
    private readonly HashSet<string> _routeSymbolIds;

    public RouteSettingContext(RouteSettingRequest request)
    {
        Request = request;
        _switchCommandsById = request.Route.SwitchCommands
            .GroupBy(static command => command.SwitchId)
            .ToDictionary(static group => group.Key, static group => group.First());
        _routeSymbolIds = request.Route.Symbols
            .Select(static symbol => symbol.Id)
            .ToHashSet();
    }

    public RouteSettingRequest Request { get; }

    /// <summary>
    /// Belegungszustand aus der Bedienebene. Aktuell kommt dieser aus der UI,
    /// spaeter kann er durch echte Rueckmelder oder eine Simulation ersetzt werden.
    /// </summary>
    public bool IsOccupied(string symbolId)
    {
        return Request.OccupiedSymbolIds.Contains(symbolId);
    }

    public bool IsLocked(string symbolId)
    {
        return Request.LockedSymbolIds.Contains(symbolId);
    }

    public DrawnTrackSymbol? FindDrawnSymbol(string symbolId)
    {
        return Request.Document.Symbols.FirstOrDefault(symbol => symbol.Id == symbolId);
    }

    public bool RouteContains(string symbolId)
    {
        return _routeSymbolIds.Contains(symbolId);
    }

    public IReadOnlyList<DrawnTrackConnection> GetDrawnConnections(string symbolId)
    {
        return Request.Document.Connections
            .Where(connection => connection.FromSymbolId == symbolId || connection.ToSymbolId == symbolId)
            .ToList();
    }

    public string? GetPortOnSymbol(DrawnTrackConnection connection, string symbolId)
    {
        if (connection.FromSymbolId == symbolId)
        {
            return connection.FromPort;
        }

        return connection.ToSymbolId == symbolId
            ? connection.ToPort
            : null;
    }

    public string? GetOtherSymbolId(DrawnTrackConnection connection, string symbolId)
    {
        if (connection.FromSymbolId == symbolId)
        {
            return connection.ToSymbolId;
        }

        return connection.ToSymbolId == symbolId
            ? connection.FromSymbolId
            : null;
    }

    public bool AnySymbolOccupied(IEnumerable<string> symbolIds)
    {
        return symbolIds.Any(IsOccupied);
    }

    public IReadOnlyList<string> ParseSymbolList(TrackSymbol symbol, string propertyName)
    {
        if (!symbol.Properties.TryGetValue(propertyName, out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            return [];
        }

        return rawValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    /// <summary>
    /// Liefert die Lage, welche die Fahrstrassensuche fuer eine Weiche ermittelt hat.
    /// Die Elementlogik kann damit pruefen, ob diese Lage erlaubt ist, und sie danach
    /// beim Stellen auf das gezeichnete Symbol anwenden.
    /// </summary>
    public bool TryGetSwitchCommand(string switchId, out SwitchCommand command)
    {
        return _switchCommandsById.TryGetValue(switchId, out command!);
    }
}
