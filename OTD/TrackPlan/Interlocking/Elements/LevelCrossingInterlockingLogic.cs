namespace OTD.TrackPlan.Interlocking.Elements;

public class LevelCrossingInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoClosedProperty = "Demo.Closed";

    public LevelCrossingInterlockingLogic()
        : base(TrackSymbolKind.LevelCrossing)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        return base.Validate(context, symbol) ??
               ValidateRequiredProperty(symbol, DemoClosedProperty, $"{symbol.Name} ist in der Demo nicht geschlossen.");
    }
}
