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

    public IReadOnlyList<SwitchCommand> SwitchCommands => _switchCommands;

    public IReadOnlySet<string> GreenSignalIds => _greenSignalIds;

    public IReadOnlySet<string> LockedSymbolIds => _lockedSymbolIds;

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
}
