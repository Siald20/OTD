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
}
