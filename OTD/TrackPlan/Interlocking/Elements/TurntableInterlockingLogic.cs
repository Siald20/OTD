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

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer Turntable hier einbauen.
        base.Apply(context, symbol, result);
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer Turntable hier einbauen.
        base.Release(context, symbol, result);
    }
}
