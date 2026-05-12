using System;
using System.Collections.Generic;
using System.Linq;

namespace OTD.TrackPlan.Interlocking.Profiles;

/// <summary>
/// Einstiegspunkt fuer Domino-67-spezifische Stellwerkslogik.
/// Aktuell nutzt das Profil noch die Default-Elementlogiken. Sobald echte Domino-67-Regeln
/// umgesetzt werden, koennen hier einzelne Logiken per ReplaceLogic(...) ausgetauscht werden,
/// z. B. eine eigene Signal-, Weichen- oder Fahrstrassenverschlusslogik.
/// </summary>
public sealed class Domino67InterlockingProfile : DefaultInterlockingProfile
{
    public Domino67InterlockingProfile()
    {
        ReplaceLogic(new Domino67SignalInterlockingLogic());
        ReplaceLogic(new Domino67ZwergSignalInterlockingLogic());
        ReplaceLogic(new Domino67TrackBlockInterlockingLogic());
        ReplaceLogic(new Domino67LineBlockInterlockingLogic());
        ReplaceLogic(new Domino67SwitchInterlockingLogic());
        ReplaceLogic(new Domino67DoubleSlipSwitchInterlockingLogic());
        ReplaceLogic(new Domino67LevelCrossingInterlockingLogic());
    }

    public override string Name => "Domino 67";

    public override RouteSettingFailure? ValidateRoute(RouteSettingContext context)
    {
        if (context.Request.Route.Symbols.Count < 2)
        {
            return new RouteSettingFailure { Message = "Domino 67: Fahrstrasse ist unvollstaendig." };
        }

        var allowedSharedBoundarySignalIds = BuildAllowedSharedBoundarySignalIds(context.Request.Route, context.Request.ActiveRoutes);

        foreach (var symbol in context.Request.Route.Symbols)
        {
            if (context.IsLocked(symbol.Id) && !allowedSharedBoundarySignalIds.Contains(symbol.Id))
            {
                return new RouteSettingFailure { Message = $"{symbol.Name} ist durch eine andere Fahrstrasse verschlossen." };
            }
        }

        foreach (var activeRoute in context.Request.ActiveRoutes)
        {
            if (RoutesConflict(context.Request.Route, activeRoute, out var conflictName))
            {
                return new RouteSettingFailure { Message = $"Fahrstrasse kollidiert mit aktiver Fahrstrasse bei {conflictName}." };
            }
        }

        var startSignal = context.Request.Route.StartSignal;
        var flankProtectionSymbols = context.ParseSymbolList(startSignal, Domino67PropertyNames.FlankProtectionSymbols);
        if (context.AnySymbolOccupied(flankProtectionSymbols))
        {
            return new RouteSettingFailure { Message = $"{startSignal.Name}: Flankenschutz ist belegt." };
        }

        var overlapSymbols = context.ParseSymbolList(startSignal, Domino67PropertyNames.OverlapSymbols);
        return context.AnySymbolOccupied(overlapSymbols)
            ? new RouteSettingFailure { Message = $"{startSignal.Name}: Durchrutschweg ist belegt." }
            : null;
    }

    public override void ApplyRoute(RouteSettingContext context, RouteSettingResultBuilder result)
    {
        foreach (var flankSwitch in FindFlankProtectionSwitches(context))
        {
            var drawnSymbol = context.FindDrawnSymbol(flankSwitch.Switch.Id);
            if (drawnSymbol is null)
            {
                continue;
            }

            drawnSymbol.CurrentSwitchPosition = flankSwitch.ProtectivePosition;
            result.AddSwitchCommand(new SwitchCommand
            {
                SwitchId = flankSwitch.Switch.Id,
                SwitchName = flankSwitch.Switch.Name,
                Position = flankSwitch.ProtectivePosition
            });
            result.LockSymbol(flankSwitch.Switch.Id);
        }
    }

    private static IReadOnlyList<FlankProtectionSwitch> FindFlankProtectionSwitches(RouteSettingContext context)
    {
        var flankSwitches = new List<FlankProtectionSwitch>();
        var seenSwitchIds = new HashSet<string>();

        foreach (var routeSymbol in context.Request.Route.Symbols)
        {
            foreach (var connection in context.GetDrawnConnections(routeSymbol.Id))
            {
                var otherSymbolId = context.GetOtherSymbolId(connection, routeSymbol.Id);
                if (otherSymbolId is null || context.RouteContains(otherSymbolId))
                {
                    continue;
                }

                var otherSymbol = context.FindDrawnSymbol(otherSymbolId);
                if (otherSymbol is null ||
                    otherSymbol.Kind is not (TrackSymbolKind.Switch or TrackSymbolKind.DoubleSlipSwitch) ||
                    !Domino67PropertyHelper.IsEnabled(ToTrackSymbol(otherSymbol), Domino67PropertyNames.FlankProtectionEnabled) ||
                    !seenSwitchIds.Add(otherSymbol.Id))
                {
                    continue;
                }

                var flankPort = context.GetPortOnSymbol(connection, otherSymbol.Id);
                if (flankPort is null ||
                    !TryFindProtectivePosition(otherSymbol, flankPort, out var protectivePosition))
                {
                    continue;
                }

                flankSwitches.Add(new FlankProtectionSwitch(otherSymbol, protectivePosition));
            }
        }

        return flankSwitches;
    }

    private static bool TryFindProtectivePosition(
        DrawnTrackSymbol switchSymbol,
        string flankPort,
        out SwitchPosition protectivePosition)
    {
        var unsafePositions = switchSymbol.SwitchRouteOptions
            .Where(option =>
                string.Equals(option.FromPort, flankPort, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(option.ToPort, flankPort, System.StringComparison.OrdinalIgnoreCase))
            .Select(static option => option.Position)
            .ToHashSet();

        var availablePositions = switchSymbol.SwitchRouteOptions
            .Select(static option => option.Position)
            .Distinct()
            .ToList();

        foreach (var position in availablePositions)
        {
            if (!unsafePositions.Contains(position))
            {
                protectivePosition = position;
                return true;
            }
        }

        protectivePosition = default;
        return false;
    }

    private sealed record FlankProtectionSwitch(
        DrawnTrackSymbol Switch,
        SwitchPosition ProtectivePosition);

    private static bool RoutesConflict(RouteResult candidate, RouteResult activeRoute, out string conflictName)
    {
        var allowedSharedBoundarySignalIds = BuildAllowedSharedBoundarySignalIds(candidate, [activeRoute]);

        var activeSymbols = activeRoute.Symbols
            .Select(static symbol => symbol.Id)
            .ToHashSet();

        foreach (var symbol in candidate.Symbols)
        {
            if (activeSymbols.Contains(symbol.Id) &&
                !allowedSharedBoundarySignalIds.Contains(symbol.Id))
            {
                conflictName = symbol.Name;
                return true;
            }
        }

        var activeConnections = activeRoute.Connections
            .SelectMany(static connection => new[]
            {
                $"{connection.FromSymbolId}->{connection.ToSymbolId}",
                $"{connection.ToSymbolId}->{connection.FromSymbolId}"
            })
            .ToHashSet();

        foreach (var connection in candidate.Connections)
        {
            if (activeConnections.Contains($"{connection.FromSymbolId}->{connection.ToSymbolId}"))
            {
                conflictName = $"{connection.FromSymbolId}-{connection.ToSymbolId}";
                return true;
            }
        }

        conflictName = string.Empty;
        return false;
    }

    private static TrackSymbol ToTrackSymbol(DrawnTrackSymbol symbol)
    {
        return new TrackSymbol
        {
            Id = symbol.Id,
            Name = symbol.Name,
            Kind = symbol.Kind,
            X = symbol.X,
            Y = symbol.Y,
            SignalDirection = symbol.SignalDirection,
            CurrentSwitchPosition = symbol.CurrentSwitchPosition,
            SwitchRouteOptions = symbol.SwitchRouteOptions,
            Properties = symbol.Properties
        };
    }

    private static bool IsSignalKind(TrackSymbolKind kind)
    {
        return kind is TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal;
    }

    private static HashSet<string> BuildAllowedSharedBoundarySignalIds(
        RouteResult candidate,
        IReadOnlyList<RouteResult> activeRoutes)
    {
        var allowedSharedBoundarySignalIds = new HashSet<string>();
        foreach (var activeRoute in activeRoutes)
        {
            if (IsSignalKind(candidate.StartSignal.Kind) &&
                IsSignalKind(activeRoute.TargetSignal.Kind) &&
                candidate.StartSignal.Id == activeRoute.TargetSignal.Id)
            {
                allowedSharedBoundarySignalIds.Add(candidate.StartSignal.Id);
            }

            if (IsSignalKind(candidate.TargetSignal.Kind) &&
                IsSignalKind(activeRoute.StartSignal.Kind) &&
                candidate.TargetSignal.Id == activeRoute.StartSignal.Id)
            {
                allowedSharedBoundarySignalIds.Add(candidate.TargetSignal.Id);
            }
        }

        return allowedSharedBoundarySignalIds;
    }
}
