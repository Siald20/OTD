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

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer Uncoupler hier einbauen.
        base.Apply(context, symbol, result);
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer Uncoupler hier einbauen.
        base.Release(context, symbol, result);
    }
}
