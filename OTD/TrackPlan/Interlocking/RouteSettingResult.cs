using System.Collections.Generic;

namespace OTD.TrackPlan.Interlocking;

/// <summary>
/// Ergebnis eines Stellversuchs.
/// Es enthaelt nicht nur Erfolg/Fehler, sondern auch die Stellwirkungen, welche
/// die UI oder spaetere Hardware-Schichten anzeigen bzw. ausfuehren koennen.
/// </summary>
public sealed class RouteSettingResult
{
    private RouteSettingResult(
        bool isSuccess,
        string message,
        IReadOnlyList<SwitchCommand> switchCommands,
        IReadOnlySet<string> greenSignalIds,
        IReadOnlySet<string> lockedSymbolIds,
        IReadOnlyList<DelayedAction> delayedActions)
    {
        IsSuccess = isSuccess;
        Message = message;
        SwitchCommands = switchCommands;
        GreenSignalIds = greenSignalIds;
        LockedSymbolIds = lockedSymbolIds;
        DelayedActions = delayedActions;
    }

    public bool IsSuccess { get; }

    public string Message { get; }

    /// <summary>
    /// Weichenbefehle, die beim Stellen ausgefuehrt wurden.
    /// </summary>
    public IReadOnlyList<SwitchCommand> SwitchCommands { get; }

    /// <summary>
    /// Signale, die nach erfolgreichem Stellen Fahrt zeigen duerfen.
    /// </summary>
    public IReadOnlySet<string> GreenSignalIds { get; }

    /// <summary>
    /// Elemente, die durch die Fahrstrasse verschlossen sind.
    /// Aktuell vorbereitet fuer spaetere Verschluss-/Aufloeselogik.
    /// </summary>
    public IReadOnlySet<string> LockedSymbolIds { get; }

    public IReadOnlyList<DelayedAction> DelayedActions { get; }

    public static RouteSettingResult Failed(string message)
    {
        return new RouteSettingResult(false, message, [], new HashSet<string>(), new HashSet<string>(), []);
    }

    public static RouteSettingResult Success(RouteSettingResultBuilder builder, string message)
    {
        return new RouteSettingResult(
            true,
            message,
            builder.SwitchCommands,
            builder.GreenSignalIds,
            builder.LockedSymbolIds,
            builder.DelayedActions);
    }
}
