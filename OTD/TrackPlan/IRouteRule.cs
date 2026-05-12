using System;

namespace OTD.TrackPlan;

public interface IRouteRule
{
    bool CanUseSymbol(TrackSymbol symbol, RouteSearchContext context);

    bool CanUseConnection(TrackConnection connection, RouteSearchContext context);

    int GetAdditionalCost(TrackConnection connection, RouteSearchContext context);
}

public sealed class RouteSearchContext
{
    public required TrackPlanGraph Graph { get; init; }

    public required TrackSymbol StartSignal { get; init; }

    public required TrackSymbol TargetSignal { get; init; }

    public required RouteSearchOptions Options { get; init; }
}

public class DefaultRouteRule : IRouteRule
{
    public virtual bool CanUseSymbol(TrackSymbol symbol, RouteSearchContext context)
    {
        if (!context.Options.AllowOccupiedSymbols && symbol.IsOccupied)
        {
            return false;
        }

        if (!context.Options.AllowLockedSymbols && symbol.IsLocked)
        {
            return false;
        }

        return symbol.Kind is not TrackSymbolKind.BufferStop || symbol.Id == context.TargetSignal.Id;
    }

    public virtual bool CanUseConnection(TrackConnection connection, RouteSearchContext context)
    {
        if (!connection.IsEnabled)
        {
            return false;
        }

        var from = context.Graph.GetSymbol(connection.FromSymbolId);
        var to = context.Graph.GetSymbol(connection.ToSymbolId);

        if (from.Kind is TrackSymbolKind.Signal or TrackSymbolKind.ZwergSignal &&
            from.Id == context.StartSignal.Id &&
            !SignalAllowsDeparture(from, to))
        {
            return false;
        }

        return true;
    }

    public virtual int GetAdditionalCost(TrackConnection connection, RouteSearchContext context)
    {
        var target = context.Graph.GetSymbol(connection.ToSymbolId);
        if (target.Kind is not (TrackSymbolKind.Switch or TrackSymbolKind.DoubleSlipSwitch))
        {
            return 0;
        }

        var requiredPosition = RouteBuilder.TryGetRequiredSwitchPosition(target, connection.FromPort, connection.ToPort);
        if (requiredPosition is null)
        {
            return 0;
        }

        return context.Options.SearchMode switch
        {
            RouteSearchMode.PreferStraightSwitches when requiredPosition == SwitchPosition.Straight => -1,
            RouteSearchMode.PreferStraightSwitches => 2,
            RouteSearchMode.MinimizeSwitchChanges when requiredPosition != target.CurrentSwitchPosition => 3,
            _ => 0
        };
    }

    public static bool SignalAllowsDeparture(TrackSymbol signal, TrackSymbol next)
    {
        var dx = next.X - signal.X;
        var dy = next.Y - signal.Y;

        return signal.SignalDirection switch
        {
            SignalDirection.Both => true,
            SignalDirection.LeftToRight => Math.Abs(dx) >= Math.Abs(dy) && dx > 0,
            SignalDirection.RightToLeft => Math.Abs(dx) >= Math.Abs(dy) && dx < 0,
            SignalDirection.TopToBottom => Math.Abs(dy) > Math.Abs(dx) && dy > 0,
            SignalDirection.BottomToTop => Math.Abs(dy) > Math.Abs(dx) && dy < 0,
            _ => true
        };
    }

    public static bool SignalIsVisibleFrom(TrackSymbol signal, TrackSymbol previous)
    {
        var dx = previous.X - signal.X;
        var dy = previous.Y - signal.Y;

        return signal.SignalDirection switch
        {
            SignalDirection.Both => true,
            SignalDirection.LeftToRight => Math.Abs(dx) >= Math.Abs(dy) && dx < 0,
            SignalDirection.RightToLeft => Math.Abs(dx) >= Math.Abs(dy) && dx > 0,
            SignalDirection.TopToBottom => Math.Abs(dy) > Math.Abs(dx) && dy < 0,
            SignalDirection.BottomToTop => Math.Abs(dy) > Math.Abs(dx) && dy > 0,
            _ => true
        };
    }

    public static bool BlockIsVisibleFrom(TrackSymbol block, TrackSymbol previous)
    {
        return SignalIsVisibleFrom(block, previous);
    }
}
