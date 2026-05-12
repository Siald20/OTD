using System;
using System.Collections.Generic;
using System.Linq;
using OTD.TrackPlan.Interlocking.Elements;
using OTD.TrackPlan.Interlocking.Profiles;

namespace OTD.TrackPlan.Interlocking;

public sealed class StationInterlockingRuntime
{
    private readonly RouteInterlockingService _interlocking;
    private readonly List<LineBlockBoundaryLink> _lineBlockBoundaryLinks = [];

    public StationInterlockingRuntime(IInterlockingProfile profile)
    {
        _interlocking = new RouteInterlockingService(profile);
    }

    public HashSet<string> OccupiedSymbolIds { get; } = [];
    public HashSet<string> ReleaseOnFreeSymbolIds { get; } = [];
    public HashSet<string> GreenSignalIds { get; } = [];
    public HashSet<string> LockedSymbolIds { get; } = [];
    public List<RouteResult> ActiveRoutes { get; } = [];
    public List<RouteResult> StoredRoutes { get; } = [];

    public IReadOnlyList<LineBlockBoundaryLink> LineBlockBoundaryLinks => _lineBlockBoundaryLinks;

    public void AddLineBlockBoundaryLink(string localLineBlockId, string remoteLineBlockId)
    {
        if (string.IsNullOrWhiteSpace(localLineBlockId) || string.IsNullOrWhiteSpace(remoteLineBlockId))
        {
            return;
        }

        if (_lineBlockBoundaryLinks.Any(link =>
                string.Equals(link.LocalLineBlockId, localLineBlockId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(link.RemoteLineBlockId, remoteLineBlockId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _lineBlockBoundaryLinks.Add(new LineBlockBoundaryLink(localLineBlockId, remoteLineBlockId));
    }

    public void RemoveLineBlockBoundaryLink(string localLineBlockId, string remoteLineBlockId)
    {
        _lineBlockBoundaryLinks.RemoveAll(link =>
            string.Equals(link.LocalLineBlockId, localLineBlockId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(link.RemoteLineBlockId, remoteLineBlockId, StringComparison.OrdinalIgnoreCase));
    }

    public void ApplyRemoteLineBlockDirection(
        string localLineBlockId,
        string remoteDirection,
        TrackPlanDocument document)
    {
        var localDirection = remoteDirection switch
        {
            Domino67PropertyNames.LineBlockDirectionOutgoing => Domino67PropertyNames.LineBlockDirectionIncoming,
            Domino67PropertyNames.LineBlockDirectionIncoming => Domino67PropertyNames.LineBlockDirectionOutgoing,
            _ => null
        };

        if (localDirection is null)
        {
            return;
        }

        SetLineBlockDirection(document, localLineBlockId, localDirection);
    }

    public void ApplyRemoteLineBlockState(
        string localLineBlockId,
        string remoteDirection,
        bool isBlocked,
        TrackPlanDocument document)
    {
        ApplyRemoteLineBlockDirection(localLineBlockId, remoteDirection, document);
        SetLineBlockBlocked(document, localLineBlockId, isBlocked);
    }

    public IReadOnlyList<LineBlockBoundaryDirectionUpdate> BuildRemoteLineBlockDirectionUpdates(TrackPlanDocument document)
    {
        var updates = new List<LineBlockBoundaryDirectionUpdate>();

        foreach (var link in _lineBlockBoundaryLinks)
        {
            var local = FindSymbol(document, link.LocalLineBlockId);
            if (local is null ||
                !local.Properties.TryGetValue(Domino67PropertyNames.LineBlockDirection, out var localDirection))
            {
                continue;
            }

            var remoteDirection = localDirection switch
            {
                Domino67PropertyNames.LineBlockDirectionOutgoing => Domino67PropertyNames.LineBlockDirectionIncoming,
                Domino67PropertyNames.LineBlockDirectionIncoming => Domino67PropertyNames.LineBlockDirectionOutgoing,
                _ => null
            };

            if (remoteDirection is null)
            {
                continue;
            }

            var isBlocked = Domino67PropertyHelper.IsEnabled(
                new TrackSymbol { Properties = local.Properties },
                Domino67PropertyNames.BlockBlocked);

            updates.Add(new LineBlockBoundaryDirectionUpdate(link.RemoteLineBlockId, remoteDirection, isBlocked));
        }

        return updates;
    }

    public bool TryApplyRoute(
        RouteResult route,
        TrackPlanDocument document,
        bool storeOnFailure,
        Func<string, bool> canStoreRoute,
        Action<RouteSettingContext, RouteSettingResultBuilder> delayedActionApplied,
        out string message,
        out IReadOnlyList<SwitchCommand> switchCommands)
    {
        var settingResult = _interlocking.TrySetRoute(CreateRouteRequest(route, document, delayedActionApplied));
        message = settingResult.Message;
        switchCommands = settingResult.SwitchCommands;

        if (!settingResult.IsSuccess)
        {
            if (storeOnFailure && canStoreRoute(settingResult.Message))
            {
                StoredRoutes.Add(route);
            }

            return false;
        }

        var storedIndex = StoredRoutes.FindIndex(storedRoute => IsSameRoute(storedRoute, route));
        if (storedIndex >= 0)
        {
            StoredRoutes.RemoveAt(storedIndex);
        }

        ActiveRoutes.Add(route);
        foreach (var symbolId in settingResult.LockedSymbolIds)
        {
            LockedSymbolIds.Add(symbolId);
        }

        foreach (var signalId in settingResult.GreenSignalIds)
        {
            GreenSignalIds.Add(signalId);
        }

        return true;
    }

    public void ReleaseAllRoutes(TrackPlanDocument document, Action<RouteSettingContext, RouteSettingResultBuilder> delayedActionApplied)
    {
        foreach (var route in ActiveRoutes.ToList())
        {
            _ = _interlocking.ReleaseRoute(CreateRouteRequest(route, document, delayedActionApplied));
        }

        ActiveRoutes.Clear();
        LockedSymbolIds.Clear();
        ReleaseOnFreeSymbolIds.Clear();
        LineBlockInterlockingLogic.RebuildStateForActiveRoutes(document, ActiveRoutes);
    }

    public List<RouteResult> ReleaseRoutesContainingSymbol(
        string symbolId,
        TrackPlanDocument document,
        Action<RouteSettingContext, RouteSettingResultBuilder> delayedActionApplied)
    {
        var affectedRoutes = ActiveRoutes
            .Where(route => route.Symbols.Any(routeSymbol => routeSymbol.Id == symbolId))
            .ToList();

        foreach (var route in affectedRoutes)
        {
            _ = _interlocking.ReleaseRoute(CreateRouteRequest(route, document, delayedActionApplied));
            ActiveRoutes.Remove(route);
        }

        RebuildLockedSymbols();
        LineBlockInterlockingLogic.RebuildStateForActiveRoutes(document, ActiveRoutes);
        return affectedRoutes;
    }

    public int TrySetStoredRoutes(
        TrackPlanDocument document,
        Func<string, bool> canStoreRoute,
        Action<RouteSettingContext, RouteSettingResultBuilder> delayedActionApplied)
    {
        var setCount = 0;
        foreach (var route in StoredRoutes.ToList())
        {
            if (TryApplyRoute(route, document, false, canStoreRoute, delayedActionApplied, out _, out _))
            {
                setCount++;
            }
        }

        return setCount;
    }

    public void RebuildLockedSymbols()
    {
        LockedSymbolIds.Clear();
        foreach (var route in ActiveRoutes)
        {
            foreach (var symbol in route.Symbols)
            {
                LockedSymbolIds.Add(symbol.Id);
            }
        }
    }

    public void ClearAllStates()
    {
        OccupiedSymbolIds.Clear();
        ReleaseOnFreeSymbolIds.Clear();
        GreenSignalIds.Clear();
        LockedSymbolIds.Clear();
        ActiveRoutes.Clear();
        StoredRoutes.Clear();
    }

    private RouteSettingRequest CreateRouteRequest(
        RouteResult route,
        TrackPlanDocument document,
        Action<RouteSettingContext, RouteSettingResultBuilder> delayedActionApplied)
    {
        return new RouteSettingRequest
        {
            Route = route,
            Document = document,
            OccupiedSymbolIds = OccupiedSymbolIds,
            ActiveRoutes = ActiveRoutes,
            LockedSymbolIds = LockedSymbolIds,
            DelayedActionApplied = delayedActionApplied
        };
    }

    private static bool IsSameRoute(RouteResult left, RouteResult right)
    {
        return left.StartSignal.Id == right.StartSignal.Id &&
               left.TargetSignal.Id == right.TargetSignal.Id;
    }

    private static void SetLineBlockDirection(TrackPlanDocument document, string symbolId, string direction)
    {
        var symbol = FindSymbol(document, symbolId);
        if (symbol is null)
        {
            return;
        }

        symbol.Properties[Domino67PropertyNames.LineBlockDirection] = direction;
    }

    private static void SetLineBlockBlocked(TrackPlanDocument document, string symbolId, bool isBlocked)
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

    private static DrawnTrackSymbol? FindSymbol(TrackPlanDocument document, string symbolId)
    {
        return document.Symbols.FirstOrDefault(symbol => symbol.Id == symbolId);
    }

}
