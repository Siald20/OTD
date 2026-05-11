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
}
