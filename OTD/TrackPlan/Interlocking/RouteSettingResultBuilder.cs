using System.Collections.Generic;

namespace OTD.TrackPlan.Interlocking;

/// <summary>
/// Sammelobjekt fuer Stellwirkungen waehrend der Apply-Phase.
/// Elementlogiken schreiben hier hinein, statt direkt UI-Zustand zu veraendern.
/// Dadurch bleibt die Stellwerkslogik testbar und unabhaengig von Avalonia.
/// </summary>
public sealed class RouteSettingResultBuilder
{
    private readonly List<SwitchCommand> _switchCommands = [];
    private readonly HashSet<string> _greenSignalIds = [];
    private readonly HashSet<string> _lockedSymbolIds = [];
    private readonly List<DelayedAction> _delayedActions = []; // Neu hinzugefügt

    public IReadOnlyList<SwitchCommand> SwitchCommands => _switchCommands;

    public IReadOnlySet<string> GreenSignalIds => _greenSignalIds;

    public IReadOnlySet<string> LockedSymbolIds => _lockedSymbolIds;

    public IReadOnlyList<DelayedAction> DelayedActions => _delayedActions; // Neu hinzugefügt

    public void AddSwitchCommand(SwitchCommand command)
    {
        _switchCommands.Add(command);
    }

    public void AddGreenSignal(string signalId)
    {
        _greenSignalIds.Add(signalId);
    }

    public void LockSymbol(string symbolId)
    {
        _lockedSymbolIds.Add(symbolId);
    }

    /// <summary>
    /// Fuegt eine Aktion hinzu, die nach einer bestimmten Verzoegerung ausgefuehrt werden soll.
    /// </summary>
    /// <param name="action">Die zu verzögernde Aktion.</param>
    public void AddDelayedAction(DelayedAction action) // Neu hinzugefügt
    {
        _delayedActions.Add(action);
    }
}
