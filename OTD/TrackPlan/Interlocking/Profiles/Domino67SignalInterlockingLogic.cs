using System;
using System.Collections.Generic;
using System.Linq;

namespace OTD.TrackPlan.Interlocking.Profiles;

public sealed class Domino67SignalInterlockingLogic : Elements.DefaultElementInterlockingLogic
{
    /// <summary>
    /// Erstellt die Domino-67-Logik fuer Signalsymbole.
    /// </summary>
    public Domino67SignalInterlockingLogic()
        : base(TrackSymbolKind.Signal)
    {
    }

    /// <summary>
    /// Sperrt das Stellen ueber Signale, die im Domino 67 als gesperrt markiert sind.
    /// </summary>
    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        var failure = base.Validate(context, symbol);
        if (failure is not null)
        {
            return failure;
        }

        return Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.TrackLocked)
            ? new RouteSettingFailure { Message = $"{symbol.Name} ist im Domino 67 gesperrt." }
            : null;
    }

    /// <summary>
    /// Startsignal: Wenn kein offener Auto-Close-BUE in der Route liegt, sofort gruen.
    /// Sonst: nach der vorgegebenen BUE-Verzoegerung erst BUE schliessen, dann Signal gruen.
    /// </summary>
    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        if (context.Request.RouteType is not RouteType.Train)
        {
            return;
        }

        if (symbol.Id != context.Request.Route.StartSignal.Id)
        {
            return;
        }

        result.LockSymbol(symbol.Id);

        if (!CanSignalLeaveRed(context, symbol))
        {
            return;
        }

        var openAutoCloseCrossings = FindOpenAutoCloseCrossings(context).ToList();
        if (openAutoCloseCrossings.Count == 0)
        {
            result.AddGreenSignal(symbol.Id);
            AddRouteZwergSignalsToGreen(context, result);
            return;
        }

        var waitDelay = openAutoCloseCrossings
            .Select(static crossing => crossing.Delay)
            .Max();

        result.AddDelayedAction(new DelayedAction(waitDelay, (ctx, delayedResult) =>
        {
            if (!CanSignalLeaveRed(ctx, symbol))
            {
                return;
            }

            foreach (var crossing in openAutoCloseCrossings)
            {
                ctx.SetSymbolProperty(crossing.SymbolId, Domino67PropertyNames.LevelCrossingClosed, true);
            }

            var currentSignal = ctx.FindDrawnSymbol(symbol.Id);
            var holdRed = currentSignal is not null
                ? Domino67PropertyHelper.IsEnabled(currentSignal, Domino67PropertyNames.HoldRed)
                : Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.HoldRed);

            if (!holdRed)
            {
                ctx.SetSymbolProperty(symbol.Id, Domino67PropertyNames.SignalIsGreen, true);
                delayedResult.AddGreenSignal(symbol.Id);
                AddRouteZwergSignalsToGreen(ctx, delayedResult);
            }
        }));
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        _ = result;
        var currentSignal = context.FindDrawnSymbol(symbol.Id);
        var keepGreen = currentSignal is not null &&
                        Domino67PropertyHelper.IsEnabled(currentSignal, Domino67PropertyNames.SignalKeepGreenOnRelease);

        if (!keepGreen)
        {
            context.SetSymbolProperty(symbol.Id, Domino67PropertyNames.SignalIsGreen, false);
        }
    }

    private static bool CanSignalLeaveRed(RouteSettingContext context, TrackSymbol symbol)
    {
        var currentSignal = context.FindDrawnSymbol(symbol.Id);
        var signalIsGreen = currentSignal is not null
            ? Domino67PropertyHelper.IsEnabled(currentSignal, Domino67PropertyNames.SignalIsGreen)
            : Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.SignalIsGreen);
        var holdRed = currentSignal is not null
            ? Domino67PropertyHelper.IsEnabled(currentSignal, Domino67PropertyNames.HoldRed)
            : Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.HoldRed);

        return !signalIsGreen && !holdRed;
    }

    private static IEnumerable<OpenAutoCloseCrossing> FindOpenAutoCloseCrossings(RouteSettingContext context)
    {
        foreach (var routeSymbol in context.Request.Route.Symbols)
        {
            if (routeSymbol.Kind is not TrackSymbolKind.LevelCrossing)
            {
                continue;
            }

            var currentSymbol = context.FindDrawnSymbol(routeSymbol.Id);
            var autoCloseEnabled = currentSymbol is not null
                ? Domino67PropertyHelper.IsEnabled(currentSymbol, Domino67PropertyNames.LevelCrossingAutoClose)
                : Domino67PropertyHelper.IsEnabled(routeSymbol, Domino67PropertyNames.LevelCrossingAutoClose);
            var isClosed = currentSymbol is not null
                ? Domino67PropertyHelper.IsEnabled(currentSymbol, Domino67PropertyNames.LevelCrossingClosed)
                : Domino67PropertyHelper.IsEnabled(routeSymbol, Domino67PropertyNames.LevelCrossingClosed);

            if (!autoCloseEnabled || isClosed)
            {
                continue;
            }

            yield return new OpenAutoCloseCrossing(
                routeSymbol.Id,
                Domino67LevelCrossingInterlockingLogic.GetAutoCloseDelay(routeSymbol));
        }
    }

    private static void AddRouteZwergSignalsToGreen(RouteSettingContext context, RouteSettingResultBuilder result)
    {
        var symbols = context.Request.Route.Symbols;
        for (var i = 0; i < symbols.Count; i++)
        {
            var routeSymbol = symbols[i];
            if (routeSymbol.Kind is not TrackSymbolKind.ZwergSignal)
            {
                continue;
            }

            // Zwergsignale nur dann auf Fahrt stellen, wenn sie in Fahrtrichtung zeigen.
            if (i + 1 >= symbols.Count || !DefaultRouteRule.SignalAllowsDeparture(routeSymbol, symbols[i + 1]))
            {
                continue;
            }

            context.SetSymbolProperty(routeSymbol.Id, Domino67PropertyNames.SignalIsGreen, true);
            result.AddGreenSignal(routeSymbol.Id);
        }
    }

    private sealed record OpenAutoCloseCrossing(string SymbolId, TimeSpan Delay);
}
