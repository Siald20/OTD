using System;
using System.Collections.Generic;
using System.Linq;

namespace OTD.TrackPlan;

public sealed class TrackPlanGraph
{
    private readonly Dictionary<string, TrackSymbol> _symbols = [];
    private readonly Dictionary<string, List<TrackConnection>> _connectionsBySymbol = [];

    public IReadOnlyCollection<TrackSymbol> Symbols => _symbols.Values;

    public IReadOnlyCollection<TrackConnection> Connections => _connectionsBySymbol.Values
        .SelectMany(static connections => connections)
        .ToList();

    public void AddSymbol(TrackSymbol symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol.Id);

        _symbols[symbol.Id] = symbol;
        _connectionsBySymbol.TryAdd(symbol.Id, []);
    }

    public void ConnectBidirectional(
        string firstSymbolId,
        string firstPort,
        string secondSymbolId,
        string secondPort,
        int cost = 1)
    {
        AddConnection(new TrackConnection
        {
            FromSymbolId = firstSymbolId,
            FromPort = firstPort,
            ToSymbolId = secondSymbolId,
            ToPort = secondPort,
            Cost = cost
        });

        AddConnection(new TrackConnection
        {
            FromSymbolId = secondSymbolId,
            FromPort = secondPort,
            ToSymbolId = firstSymbolId,
            ToPort = firstPort,
            Cost = cost
        });
    }

    public void AddConnection(TrackConnection connection)
    {
        if (!_symbols.ContainsKey(connection.FromSymbolId))
        {
            throw new InvalidOperationException($"Symbol '{connection.FromSymbolId}' ist nicht im Gleisplan vorhanden.");
        }

        if (!_symbols.ContainsKey(connection.ToSymbolId))
        {
            throw new InvalidOperationException($"Symbol '{connection.ToSymbolId}' ist nicht im Gleisplan vorhanden.");
        }

        _connectionsBySymbol.TryAdd(connection.FromSymbolId, []);
        _connectionsBySymbol[connection.FromSymbolId].Add(connection);
    }

    public TrackSymbol GetSymbol(string symbolId)
    {
        return _symbols.TryGetValue(symbolId, out var symbol)
            ? symbol
            : throw new InvalidOperationException($"Symbol '{symbolId}' ist nicht im Gleisplan vorhanden.");
    }

    public bool TryGetSymbol(string symbolId, out TrackSymbol? symbol)
    {
        return _symbols.TryGetValue(symbolId, out symbol);
    }

    public IReadOnlyList<TrackConnection> GetConnections(string symbolId)
    {
        return _connectionsBySymbol.TryGetValue(symbolId, out var connections)
            ? connections
            : [];
    }
}
