namespace OTD.TrackPlan.Interlocking.Elements;

public sealed class ZwergSignalInterlockingLogic : DefaultElementInterlockingLogic
{
    public ZwergSignalInterlockingLogic()
        : base(TrackSymbolKind.ZwergSignal)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        return base.Validate(context, symbol);
    }

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer Zwergsignal hier erweitern.
        result.LockSymbol(symbol.Id);

        // Zwergsignale gelten fuer Rangierfahrstrassen.
        if (context.Request.RouteType is RouteType.Shunting)
        {
            result.AddGreenSignal(symbol.Id);
        }
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer Zwergsignal hier einbauen.
        base.Release(context, symbol, result);
    }
}
