namespace OTD.TrackPlan.Interlocking.Elements;

public sealed class PlatformInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoPassengerStopOnlyProperty = "Demo.PassengerStopOnly";

    public PlatformInterlockingLogic()
        : base(TrackSymbolKind.Platform)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        return base.Validate(context, symbol) ??
               (IsPropertyEnabled(symbol, DemoPassengerStopOnlyProperty)
                   ? new RouteSettingFailure { Message = $"{symbol.Name} ist in der Demo nur fuer haltende Zuege erlaubt." }
                   : null);
    }

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer Platform hier einbauen.
        base.Apply(context, symbol, result);
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer Platform hier einbauen.
        base.Release(context, symbol, result);
    }
}
