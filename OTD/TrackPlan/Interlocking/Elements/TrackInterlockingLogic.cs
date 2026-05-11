namespace OTD.TrackPlan.Interlocking.Elements;

public sealed class TrackInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoMaintenanceProperty = "Demo.Maintenance";

    public TrackInterlockingLogic()
        : base(TrackSymbolKind.Track)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        return base.Validate(context, symbol) ??
               (IsPropertyEnabled(symbol, DemoMaintenanceProperty)
                   ? new RouteSettingFailure { Message = $"{symbol.Name} ist im Demo-Unterhalt." }
                   : null);
    }
}
