namespace OTD.TrackPlan.Interlocking.Elements;

public sealed class UncouplerInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoLoweredProperty = "Demo.Lowered";

    public UncouplerInterlockingLogic()
        : base(TrackSymbolKind.Uncoupler)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        return base.Validate(context, symbol) ??
               ValidateRequiredProperty(symbol, DemoLoweredProperty, $"{symbol.Name} ist in der Demo nicht abgesenkt.");
    }
}
