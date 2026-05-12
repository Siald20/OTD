using System;

namespace OTD.TrackPlan.Interlocking;

/// <summary>
/// Repraesentiert eine Aktion, die nach einer bestimmten Verzoegerung ausgefuehrt werden soll.
/// </summary>
public sealed class DelayedAction
{
    /// <summary>
    /// Die Zeitspanne, nach der die Aktion ausgefuehrt werden soll.
    /// </summary>
    public TimeSpan Delay { get; }

    /// <summary>
    /// Die auszufuehrende Aktion. Der RouteSettingContext wird uebergeben,
    /// um Zugriff auf den aktuellen Zustand und Hilfsfunktionen zu haben.
    /// </summary>
    public Action<RouteSettingContext, RouteSettingResultBuilder> Action { get; }

    public DelayedAction(TimeSpan delay, Action<RouteSettingContext, RouteSettingResultBuilder> action)
    {
        Delay = delay;
        Action = action;
    }
}
