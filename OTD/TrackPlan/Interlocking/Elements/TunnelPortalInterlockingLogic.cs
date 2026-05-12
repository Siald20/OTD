namespace OTD.TrackPlan.Interlocking.Elements;

public sealed class TunnelPortalInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoTunnelClearProperty = "Demo.TunnelClear";

    public TunnelPortalInterlockingLogic()
        : base(TrackSymbolKind.TunnelPortal)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        return base.Validate(context, symbol) ??
               ValidateRequiredProperty(symbol, DemoTunnelClearProperty, $"{symbol.Name} ist in der Demo nicht frei.");
    }

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer TunnelPortal hier einbauen.
        base.Apply(context, symbol, result);
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer TunnelPortal hier einbauen.
        base.Release(context, symbol, result);
    }
}
