namespace OTD.TrackPlan.Interlocking.Elements;

public sealed class BridgeInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoBridgeReleasedProperty = "Demo.BridgeReleased";

    public BridgeInterlockingLogic()
        : base(TrackSymbolKind.Bridge)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        return base.Validate(context, symbol) ??
               ValidateRequiredProperty(symbol, DemoBridgeReleasedProperty, $"{symbol.Name} ist in der Demo nicht freigegeben.");
    }

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer Bridge hier einbauen.
        base.Apply(context, symbol, result);
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer Bridge hier einbauen.
        base.Release(context, symbol, result);
    }
}
