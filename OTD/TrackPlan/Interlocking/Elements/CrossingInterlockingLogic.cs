namespace OTD.TrackPlan.Interlocking.Elements;

public sealed class CrossingInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoConflictingCrossingProperty = "Demo.ConflictingCrossing";

    public CrossingInterlockingLogic()
        : base(TrackSymbolKind.Crossing)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        return base.Validate(context, symbol) ??
               (IsPropertyEnabled(symbol, DemoConflictingCrossingProperty)
                   ? new RouteSettingFailure { Message = $"{symbol.Name} hat einen Demo-Kreuzungskonflikt." }
                   : null);
    }

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer Crossing hier einbauen.
        base.Apply(context, symbol, result);
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer Crossing hier einbauen.
        base.Release(context, symbol, result);
    }
}
