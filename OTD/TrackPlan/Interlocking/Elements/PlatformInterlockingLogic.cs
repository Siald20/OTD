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
}
