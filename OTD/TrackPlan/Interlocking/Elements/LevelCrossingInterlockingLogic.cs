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

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer LevelCrossing hier einbauen.
        base.Apply(context, symbol, result);
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer LevelCrossing hier einbauen.
        base.Release(context, symbol, result);
    }
}
