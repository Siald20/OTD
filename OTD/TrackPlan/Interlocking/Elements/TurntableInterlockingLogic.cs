namespace OTD.TrackPlan.Interlocking.Elements;

public sealed class TurntableInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoAlignedProperty = "Demo.Aligned";

    public TurntableInterlockingLogic()
        : base(TrackSymbolKind.Turntable)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        return base.Validate(context, symbol) ??
               ValidateRequiredProperty(symbol, DemoAlignedProperty, $"{symbol.Name} ist in der Demo nicht ausgerichtet.");
    }
}
