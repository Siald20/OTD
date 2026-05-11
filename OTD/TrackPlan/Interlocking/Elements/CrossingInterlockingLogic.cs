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
}
