using System;
using System.Collections.Generic;
using System.Linq;
using OTD.TrackPlan.Interlocking.Profiles;

namespace OTD.TrackPlan.Interlocking.Elements;

public class LineBlockInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoReserveOnlyProperty = "Demo.ReserveOnly";

    public LineBlockInterlockingLogic()
        : base(TrackSymbolKind.LineBlock)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        var currentSymbol = context.FindDrawnSymbol(symbol.Id);
        var effectiveSymbol = currentSymbol is null
            ? symbol
            : new TrackSymbol
            {
                Id = currentSymbol.Id,
                Name = currentSymbol.Name,
                Kind = currentSymbol.Kind,
                X = currentSymbol.X,
                Y = currentSymbol.Y,
                SignalDirection = currentSymbol.SignalDirection,
                CurrentSwitchPosition = currentSymbol.CurrentSwitchPosition,
                SwitchRouteOptions = currentSymbol.SwitchRouteOptions,
                Properties = currentSymbol.Properties
            };

        var baseFailure = base.Validate(context, symbol);
        if (baseFailure is not null)
        {
            return baseFailure;
        }

        var pairedLineBlockIds = GetPairedLineBlockIds(context.Request.Document, symbol.Id);
        var isAnyGroupedLineBlockBlocked = pairedLineBlockIds
            .Select(context.FindDrawnSymbol)
            .Where(static grouped => grouped is not null)
            .Select(static grouped => new TrackSymbol { Properties = grouped!.Properties })
            .Any(groupedSymbol => Domino67PropertyHelper.IsEnabled(groupedSymbol, Domino67PropertyNames.BlockBlocked));

        if (isAnyGroupedLineBlockBlocked || Domino67PropertyHelper.IsEnabled(effectiveSymbol, Domino67PropertyNames.BlockBlocked))
        {
            return new RouteSettingFailure { Message = $"{symbol.Name} ist durch Gegenrichtung verriegelt." };
        }

        if (IsPropertyEnabled(effectiveSymbol, DemoReserveOnlyProperty))
        {
            return new RouteSettingFailure { Message = $"{symbol.Name} ist nur fuer Reservemanoever freigegeben." };
        }

        if (string.Equals(context.Request.Route.TargetSignal.Id, symbol.Id, StringComparison.OrdinalIgnoreCase))
        {
            var boundaryDirection = effectiveSymbol.Properties.TryGetValue(Domino67PropertyNames.LineBlockDirection, out var storedBoundaryDirection)
                ? storedBoundaryDirection
                : Domino67PropertyNames.LineBlockDirectionOutgoing;
            if (!string.Equals(boundaryDirection, Domino67PropertyNames.LineBlockDirectionOutgoing, StringComparison.OrdinalIgnoreCase))
            {
                return new RouteSettingFailure { Message = $"{symbol.Name} ist nicht auf AB gestellt." };
            }

            // Fahrtrichtung wird beim Stellen automatisch angepasst.
        }

        if (context.Request.RouteType is RouteType.Shunting)
        {
            return null;
        }

        return context.IsOccupied(symbol.Id)
            ? new RouteSettingFailure { Message = $"{symbol.Name} ist belegt." }
            : null;
    }

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer LineBlock hier erweitern.
        base.Apply(context, symbol, result);
        ApplyStateForRoute(context);
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer LineBlock hier erweitern.
        _ = result;
        var drawnSymbol = context.FindDrawnSymbol(symbol.Id);
        if (drawnSymbol?.Kind is TrackSymbolKind.LineBlock)
        {
            drawnSymbol.Properties.Remove(Domino67PropertyNames.LineBlockDirection);
            drawnSymbol.Properties.Remove(Domino67PropertyNames.BlockBlocked);
        }
    }

    public static void RebuildStateForActiveRoutes(TrackPlanDocument document, IReadOnlyList<RouteResult> activeRoutes)
    {
        foreach (var lineBlock in document.Symbols.Where(static symbol => symbol.Kind is TrackSymbolKind.LineBlock))
        {
            lineBlock.Properties.Remove(Domino67PropertyNames.LineBlockDirection);
            lineBlock.Properties.Remove(Domino67PropertyNames.BlockBlocked);
        }

        foreach (var route in activeRoutes)
        {
            ApplyStateForRoute(document, route);
        }
    }

    private static void ApplyStateForRoute(RouteSettingContext context)
    {
        ApplyStateForRoute(context.Request.Document, context.Request.Route);
    }

    private static void ApplyStateForRoute(TrackPlanDocument document, RouteResult route)
    {
        if (route.TargetSignal.Kind is TrackSymbolKind.LineBlock)
        {
            SetDirection(document, route.TargetSignal.Id, Domino67PropertyNames.LineBlockDirectionOutgoing);
            SetTravelDirectionForTarget(document, route, route.TargetSignal.Id);
            SetBlocked(document, route.TargetSignal.Id, true);
            SetConnectedLineBlockDirections(document, route.TargetSignal.Id, Domino67PropertyNames.LineBlockDirectionIncoming);
            SetConnectedLineBlockBlocked(document, route.TargetSignal.Id, true);
        }

        foreach (var connection in route.Connections)
        {
            var from = FindSymbol(document, connection.FromSymbolId);
            var to = FindSymbol(document, connection.ToSymbolId);
            if (from?.Kind is not TrackSymbolKind.LineBlock || to?.Kind is not TrackSymbolKind.LineBlock)
            {
                continue;
            }

            SetDirection(document, from.Id, Domino67PropertyNames.LineBlockDirectionOutgoing);
            SetDirection(document, to.Id, Domino67PropertyNames.LineBlockDirectionIncoming);
            SetTravelDirection(document, from.Id, to);
            SetTravelDirection(document, to.Id, from);
            SetBlocked(document, from.Id, true);
            SetBlocked(document, to.Id, true);
        }
    }

    private static void SetConnectedLineBlockDirections(TrackPlanDocument document, string lineBlockId, string direction)
    {
        foreach (var connection in document.Connections)
        {
            var otherSymbolId = connection.FromSymbolId == lineBlockId
                ? connection.ToSymbolId
                : connection.ToSymbolId == lineBlockId
                    ? connection.FromSymbolId
                    : null;
            if (otherSymbolId is null)
            {
                continue;
            }

            var other = FindSymbol(document, otherSymbolId);
            if (other?.Kind is TrackSymbolKind.LineBlock)
            {
                SetDirection(document, other.Id, direction);
            }
        }
    }

    private static void SetConnectedLineBlockBlocked(TrackPlanDocument document, string lineBlockId, bool isBlocked)
    {
        foreach (var groupedLineBlockId in GetPairedLineBlockIds(document, lineBlockId))
        {
            SetBlocked(document, groupedLineBlockId, isBlocked);
        }
    }

    private static HashSet<string> GetPairedLineBlockIds(TrackPlanDocument document, string rootLineBlockId)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        visited.Add(rootLineBlockId);

        foreach (var connection in document.Connections)
        {
            var otherId = connection.FromSymbolId == rootLineBlockId
                ? connection.ToSymbolId
                : connection.ToSymbolId == rootLineBlockId
                    ? connection.FromSymbolId
                    : null;
            if (otherId is null)
            {
                continue;
            }

            var otherSymbol = FindSymbol(document, otherId);
            if (otherSymbol?.Kind is TrackSymbolKind.LineBlock)
            {
                visited.Add(otherSymbol.Id);
            }
        }

        return visited;
    }

    private static void SetDirection(TrackPlanDocument document, string symbolId, string direction)
    {
        var symbol = FindSymbol(document, symbolId);
        if (symbol is null)
        {
            return;
        }

        symbol.Properties[Domino67PropertyNames.LineBlockDirection] = direction;
    }

    private static void SetBlocked(TrackPlanDocument document, string symbolId, bool isBlocked)
    {
        var symbol = FindSymbol(document, symbolId);
        if (symbol is null)
        {
            return;
        }

        if (isBlocked)
        {
            symbol.Properties[Domino67PropertyNames.BlockBlocked] = "true";
            return;
        }

        symbol.Properties.Remove(Domino67PropertyNames.BlockBlocked);
    }

    private static void SetTravelDirectionForTarget(TrackPlanDocument document, RouteResult route, string targetId)
    {
        var required = GetRequiredTravelDirectionForTarget(document, route, targetId);
        if (required is null)
        {
            return;
        }

        var symbol = FindSymbol(document, targetId);
        if (symbol is null)
        {
            return;
        }

        symbol.Properties[Domino67PropertyNames.LineBlockTravelDirection] = required;
    }

    private static string? GetRequiredTravelDirectionForTarget(TrackPlanDocument document, RouteResult route, string targetId)
    {
        var incomingConnection = route.Connections.LastOrDefault(connection =>
            string.Equals(connection.ToSymbolId, targetId, StringComparison.OrdinalIgnoreCase));
        if (incomingConnection is null)
        {
            return null;
        }

        var target = FindSymbol(document, targetId);
        var source = FindSymbol(document, incomingConnection.FromSymbolId);
        if (target is null || source is null)
        {
            return null;
        }

        return GetDirectionValue(source, target);
    }

    private static void SetTravelDirection(TrackPlanDocument document, string symbolId, DrawnTrackSymbol neighbor)
    {
        var symbol = FindSymbol(document, symbolId);
        if (symbol is null)
        {
            return;
        }

        symbol.Properties[Domino67PropertyNames.LineBlockTravelDirection] = GetDirectionValue(symbol, neighbor);
    }

    private static string GetDirectionValue(DrawnTrackSymbol from, DrawnTrackSymbol to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            return dx >= 0 ? SignalDirection.LeftToRight.ToString() : SignalDirection.RightToLeft.ToString();
        }

        return dy >= 0 ? SignalDirection.TopToBottom.ToString() : SignalDirection.BottomToTop.ToString();
    }

    private static DrawnTrackSymbol? FindSymbol(TrackPlanDocument document, string symbolId)
    {
        return document.Symbols.FirstOrDefault(symbol =>
            string.Equals(symbol.Id, symbolId, StringComparison.OrdinalIgnoreCase));
    }
}
