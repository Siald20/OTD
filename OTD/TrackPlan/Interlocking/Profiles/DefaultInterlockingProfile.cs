using System;
using System.Collections.Generic;
using OTD.TrackPlan.Interlocking.Elements;

namespace OTD.TrackPlan.Interlocking.Profiles;

/// <summary>
/// Standardprofil fuer die Stellwerkslogik.
/// Ein Profil ist die Zuordnung "Elementtyp -> Logik". Fuer Domino 67 oder andere
/// Stellwerksarten kann ein Profil einzelne Elementlogiken ersetzen, ohne RouteBuilder,
/// UI oder Dokumentmodell anzufassen.
/// </summary>
public class DefaultInterlockingProfile : IInterlockingProfile
{
    private readonly Dictionary<TrackSymbolKind, IInterlockingElementLogic> _logicByKind;
    private readonly IInterlockingElementLogic _fallbackLogic = new TrackInterlockingLogic();

    public DefaultInterlockingProfile()
    {
        _logicByKind = new Dictionary<TrackSymbolKind, IInterlockingElementLogic>
        {
            [TrackSymbolKind.Track] = new TrackInterlockingLogic(),
            [TrackSymbolKind.TrackBlock] = new TrackBlockInterlockingLogic(),
            [TrackSymbolKind.LineBlock] = new LineBlockInterlockingLogic(),
            [TrackSymbolKind.Signal] = new SignalInterlockingLogic(),
            [TrackSymbolKind.ZwergSignal] = new ZwergSignalInterlockingLogic(),
            [TrackSymbolKind.Switch] = new SwitchInterlockingLogic(),
            [TrackSymbolKind.DoubleSlipSwitch] = new DoubleSlipSwitchInterlockingLogic(),
            [TrackSymbolKind.Crossing] = new CrossingInterlockingLogic(),
            [TrackSymbolKind.BufferStop] = new BufferStopInterlockingLogic(),
            [TrackSymbolKind.Sensor] = new SensorInterlockingLogic(),
            [TrackSymbolKind.Platform] = new PlatformInterlockingLogic(),
            [TrackSymbolKind.LevelCrossing] = new LevelCrossingInterlockingLogic(),
            [TrackSymbolKind.TunnelPortal] = new TunnelPortalInterlockingLogic(),
            [TrackSymbolKind.Bridge] = new BridgeInterlockingLogic(),
            [TrackSymbolKind.Uncoupler] = new UncouplerInterlockingLogic(),
            [TrackSymbolKind.Depot] = new DepotInterlockingLogic(),
            [TrackSymbolKind.Turntable] = new TurntableInterlockingLogic(),
            [TrackSymbolKind.TextLabel] = new TextLabelInterlockingLogic()
        };
    }

    public virtual string Name => "Default";

    public virtual RouteSettingFailure? ValidateRoute(RouteSettingContext context)
    {
        return null;
    }

    public virtual void ApplyRoute(RouteSettingContext context, RouteSettingResultBuilder result)
    {
    }

    public virtual void ReleaseRoute(RouteSettingContext context, RouteSettingResultBuilder result)
    {
    }

    public virtual IInterlockingElementLogic GetLogic(TrackSymbolKind kind)
    {
        return _logicByKind.TryGetValue(kind, out var logic)
            ? logic
            : _fallbackLogic;
    }

    /// <summary>
    /// Erweiterungspunkt fuer abgeleitete Profile. Domino67InterlockingProfile kann
    /// z. B. nur die Signal- oder Weichenlogik ersetzen und alle anderen Default-Regeln behalten.
    /// </summary>
    protected void ReplaceLogic(IInterlockingElementLogic logic)
    {
        ArgumentNullException.ThrowIfNull(logic);
        _logicByKind[logic.Kind] = logic;
    }
}
