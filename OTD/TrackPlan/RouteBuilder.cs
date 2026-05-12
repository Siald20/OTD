using System;
using System.Collections.Generic;
using System.Linq;

namespace OTD.TrackPlan;

public sealed class RouteBuilder
{
    public IReadOnlyList<RouteResult> FindAllRoutes(
        TrackPlanGraph graph,
        RouteSearchOptions? options = null)
    {
        options ??= new RouteSearchOptions();

        var signals = graph.Symbols
            .Where(static symbol => symbol.Kind is TrackSymbolKind.Signal)
            .ToList();

        var routes = new List<RouteResult>();
        foreach (var startSignal in signals)
        {
            routes.AddRange(FindRoutesToNextVisibleSignals(graph, startSignal, options));
        }

        return routes
            .OrderBy(static route => route.StartSignal.Name)
            .ThenBy(static route => route.TargetSignal.Name)
            .ThenBy(static route => route.Cost)
            .ToList();
    }

    private static IReadOnlyList<RouteResult> FindRoutesToNextVisibleSignals(
        TrackPlanGraph graph,
        TrackSymbol startSignal,
        RouteSearchOptions options)
    {
        var context = new RouteSearchContext
        {
            Graph = graph,
            StartSignal = startSignal,
            TargetSignal = startSignal,
            Options = options
        };

        if (!options.Rule.CanUseSymbol(startSignal, context))
        {
            return [];
        }

        var routes = new List<RouteResult>();
        var path = new List<RouteNode>
        {
            new(startSignal.Id, null, null, 0)
        };
        var visited = new HashSet<string> { startSignal.Id };
        var visitedCount = 0;

        SearchToNextVisibleSignals(graph, startSignal.Id, context, path, visited, routes, ref visitedCount);

        return routes;
    }

    public IReadOnlyList<RouteResult> FindAllRoutes(
        TrackPlanGraph graph,
        string startSignalId,
        string targetSignalId,
        RouteSearchOptions? options = null)
    {
        options ??= new RouteSearchOptions();

        if (!TryCreateContext(graph, startSignalId, targetSignalId, options, out var context))
        {
            return [];
        }

        var routes = new List<RouteResult>();
        var path = new List<RouteNode>
        {
            new(context.StartSignal.Id, null, null, 0)
        };
        var visited = new HashSet<string> { context.StartSignal.Id };
        var visitedCount = 0;

        SearchAll(graph, context.StartSignal.Id, context.TargetSignal.Id, context, path, visited, routes, ref visitedCount);

        return routes
            .OrderBy(static route => route.Cost)
            .ToList();
    }

    public RouteSearchResult FindRoute(
        TrackPlanGraph graph,
        string startSignalId,
        string targetSignalId,
        RouteSearchOptions? options = null)
    {
        options ??= new RouteSearchOptions();

        if (!TryCreateContext(graph, startSignalId, targetSignalId, options, out var context))
        {
            return RouteSearchResult.Failed("Start- oder Zielsignal ist ungueltig oder durch Regeln gesperrt.");
        }

        return Search(graph, context.StartSignal, context.TargetSignal, context);
    }

    private static bool TryCreateContext(
        TrackPlanGraph graph,
        string startSignalId,
        string targetSignalId,
        RouteSearchOptions options,
        out RouteSearchContext context)
    {
        context = null!;

        if (!graph.TryGetSymbol(startSignalId, out var startSignal) ||
            startSignal is null ||
            startSignal.Kind is not TrackSymbolKind.Signal)
        {
            return false;
        }

        if (!graph.TryGetSymbol(targetSignalId, out var targetSignal) ||
            targetSignal is null ||
            !IsRouteTarget(targetSignal))
        {
            return false;
        }

        context = new RouteSearchContext
        {
            Graph = graph,
            StartSignal = startSignal,
            TargetSignal = targetSignal,
            Options = options
        };

        return options.Rule.CanUseSymbol(startSignal, context) &&
               options.Rule.CanUseSymbol(targetSignal, context);
    }

    private static RouteSearchResult Search(
        TrackPlanGraph graph,
        TrackSymbol startSignal,
        TrackSymbol targetSignal,
        RouteSearchContext context)
    {
        var queue = new PriorityQueue<RouteNode, int>();
        var bestCostBySymbol = new Dictionary<string, int>();

        queue.Enqueue(new RouteNode(startSignal.Id, null, null, 0), 0);
        bestCostBySymbol[startSignal.Id] = 0;

        var visited = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visited++;

            if (visited > context.Options.MaxVisitedSymbols)
            {
                return RouteSearchResult.Failed("Fahrstraßensuche abgebrochen: zu viele Symbole besucht.");
            }

            if (current.SymbolId == targetSignal.Id)
            {
                return RouteSearchResult.Success(BuildRoute(graph, current, startSignal, targetSignal));
            }

            foreach (var connection in graph.GetConnections(current.SymbolId))
            {
                if (!context.Options.Rule.CanUseConnection(connection, context))
                {
                    continue;
                }

                if (!CanLeaveCurrentSymbol(graph, current.IncomingConnection, connection))
                {
                    continue;
                }

                var nextSymbol = graph.GetSymbol(connection.ToSymbolId);
                if (nextSymbol.Id != targetSignal.Id && !context.Options.Rule.CanUseSymbol(nextSymbol, context))
                {
                    continue;
                }

                var additionalCost = context.Options.Rule.GetAdditionalCost(connection, context);
                var nextCost = current.Cost + Math.Max(1, connection.Cost + additionalCost);

                if (bestCostBySymbol.TryGetValue(nextSymbol.Id, out var bestKnownCost) &&
                    bestKnownCost <= nextCost)
                {
                    continue;
                }

                bestCostBySymbol[nextSymbol.Id] = nextCost;
                queue.Enqueue(new RouteNode(nextSymbol.Id, current, connection, nextCost), nextCost);
            }
        }

        return RouteSearchResult.Failed($"Keine Fahrstraße von '{startSignal}' nach '{targetSignal}' gefunden.");
    }

    private static void SearchAll(
        TrackPlanGraph graph,
        string currentSymbolId,
        string targetSignalId,
        RouteSearchContext context,
        List<RouteNode> path,
        HashSet<string> visited,
        List<RouteResult> routes,
        ref int visitedCount)
    {
        visitedCount++;
        if (visitedCount > context.Options.MaxVisitedSymbols)
        {
            return;
        }

        if (currentSymbolId == targetSignalId)
        {
            routes.Add(BuildRoute(graph, path[^1], context.StartSignal, context.TargetSignal));
            return;
        }

        foreach (var connection in graph.GetConnections(currentSymbolId))
        {
            if (!context.Options.Rule.CanUseConnection(connection, context) ||
                visited.Contains(connection.ToSymbolId))
            {
                continue;
            }

            if (!CanLeaveCurrentSymbol(graph, path[^1].IncomingConnection, connection))
            {
                continue;
            }

            var nextSymbol = graph.GetSymbol(connection.ToSymbolId);
            if (nextSymbol.Id != targetSignalId && !context.Options.Rule.CanUseSymbol(nextSymbol, context))
            {
                continue;
            }

            var additionalCost = context.Options.Rule.GetAdditionalCost(connection, context);
            var nextCost = path[^1].Cost + Math.Max(1, connection.Cost + additionalCost);
            var node = new RouteNode(nextSymbol.Id, path[^1], connection, nextCost);

            path.Add(node);
            visited.Add(nextSymbol.Id);
            SearchAll(graph, nextSymbol.Id, targetSignalId, context, path, visited, routes, ref visitedCount);
            visited.Remove(nextSymbol.Id);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static void SearchToNextVisibleSignals(
        TrackPlanGraph graph,
        string currentSymbolId,
        RouteSearchContext context,
        List<RouteNode> path,
        HashSet<string> visited,
        List<RouteResult> routes,
        ref int visitedCount)
    {
        visitedCount++;
        if (visitedCount > context.Options.MaxVisitedSymbols)
        {
            return;
        }

        var currentSymbol = graph.GetSymbol(currentSymbolId);

        foreach (var connection in graph.GetConnections(currentSymbolId))
        {
            if (!context.Options.Rule.CanUseConnection(connection, context) ||
                visited.Contains(connection.ToSymbolId))
            {
                continue;
            }

            if (!CanLeaveCurrentSymbol(graph, path[^1].IncomingConnection, connection))
            {
                continue;
            }

            var nextSymbol = graph.GetSymbol(connection.ToSymbolId);
            if (!context.Options.Rule.CanUseSymbol(nextSymbol, context))
            {
                continue;
            }

            var additionalCost = context.Options.Rule.GetAdditionalCost(connection, context);
            var nextCost = path[^1].Cost + Math.Max(1, connection.Cost + additionalCost);
            var node = new RouteNode(nextSymbol.Id, path[^1], connection, nextCost);

            path.Add(node);
            visited.Add(nextSymbol.Id);

            if (IsRouteTarget(nextSymbol) &&
                nextSymbol.Id != context.StartSignal.Id &&
                IsRouteTargetVisibleFrom(nextSymbol, currentSymbol))
            {
                routes.Add(BuildRoute(graph, node, context.StartSignal, nextSymbol));
            }
            else
            {
                SearchToNextVisibleSignals(graph, nextSymbol.Id, context, path, visited, routes, ref visitedCount);
            }

            visited.Remove(nextSymbol.Id);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static RouteResult BuildRoute(
        TrackPlanGraph graph,
        RouteNode targetNode,
        TrackSymbol startSignal,
        TrackSymbol targetSignal)
    {
        var nodes = new Stack<RouteNode>();
        var current = targetNode;

        while (current is not null)
        {
            nodes.Push(current);
            current = current.Previous;
        }

        var symbols = new List<TrackSymbol>();
        var connections = new List<TrackConnection>();

        foreach (var node in nodes)
        {
            symbols.Add(graph.GetSymbol(node.SymbolId));
            if (node.IncomingConnection is not null)
            {
                connections.Add(node.IncomingConnection);
            }
        }

        return new RouteResult
        {
            StartSignal = startSignal,
            TargetSignal = targetSignal,
            Symbols = symbols,
            Connections = connections,
            SwitchCommands = BuildSwitchCommands(graph, connections),
            Cost = targetNode.Cost
        };
    }

    private static IReadOnlyList<SwitchCommand> BuildSwitchCommands(
        TrackPlanGraph graph,
        IReadOnlyList<TrackConnection> connections)
    {
        var commands = new List<SwitchCommand>();

        for (var i = 0; i < connections.Count; i++)
        {
            var connection = connections[i];
            var symbol = graph.GetSymbol(connection.ToSymbolId);

            if (symbol.Kind is not (TrackSymbolKind.Switch or TrackSymbolKind.DoubleSlipSwitch) || i + 1 >= connections.Count)
            {
                continue;
            }

            var nextConnection = connections[i + 1];
            var position = TryGetRequiredSwitchPosition(symbol, connection.ToPort, nextConnection.FromPort);
            if (position is null)
            {
                continue;
            }

            commands.Add(new SwitchCommand
            {
                SwitchId = symbol.Id,
                SwitchName = symbol.Name,
                Position = position.Value
            });
        }

        return commands;
    }

    public static SwitchPosition? TryGetRequiredSwitchPosition(
        TrackSymbol switchSymbol,
        string fromPort,
        string toPort)
    {
        if (switchSymbol.Kind is not (TrackSymbolKind.Switch or TrackSymbolKind.DoubleSlipSwitch))
        {
            return null;
        }

        return switchSymbol.SwitchRouteOptions
            .FirstOrDefault(option => option.Matches(fromPort, toPort))?
            .Position;
    }

    private static bool CanLeaveCurrentSymbol(
        TrackPlanGraph graph,
        TrackConnection? incomingConnection,
        TrackConnection outgoingConnection)
    {
        if (incomingConnection is null)
        {
            return true;
        }

        var currentSymbol = graph.GetSymbol(outgoingConnection.FromSymbolId);
        if (currentSymbol.Kind is not (TrackSymbolKind.Switch or TrackSymbolKind.DoubleSlipSwitch))
        {
            return true;
        }

        return TryGetRequiredSwitchPosition(
            currentSymbol,
            incomingConnection.ToPort,
            outgoingConnection.FromPort) is not null;
    }

    private sealed record RouteNode(
        string SymbolId,
        RouteNode? Previous,
        TrackConnection? IncomingConnection,
        int Cost);

    private static bool IsRouteTarget(TrackSymbol symbol)
    {
        return symbol.Kind is TrackSymbolKind.Signal or TrackSymbolKind.LineBlock or TrackSymbolKind.BufferStop;
    }

    private static bool IsRouteTargetVisibleFrom(TrackSymbol target, TrackSymbol previous)
    {
        return target.Kind switch
        {
            TrackSymbolKind.Signal => DefaultRouteRule.SignalIsVisibleFrom(target, previous),
            TrackSymbolKind.LineBlock => DefaultRouteRule.BlockIsVisibleFrom(target, previous),
            TrackSymbolKind.BufferStop => true,
            _ => false
        };
    }
}
