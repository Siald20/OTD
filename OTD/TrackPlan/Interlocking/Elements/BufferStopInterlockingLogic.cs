namespace OTD.TrackPlan.Interlocking.Elements;

public sealed class BufferStopInterlockingLogic : DefaultElementInterlockingLogic
{
    public BufferStopInterlockingLogic()
        : base(TrackSymbolKind.BufferStop)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        var failure = base.Validate(context, symbol);
        if (failure is not null)
        {
            return failure;
        }

        // Prellbock ist als Fahrstrassenziel erlaubt, aber nicht als Durchgangselement.
        return symbol.Id == context.Request.Route.TargetSignal.Id
            ? null
            : new RouteSettingFailure { Message = $"{symbol.Name} kann nur als Fahrstrassenziel verwendet werden." };
    }

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer BufferStop hier einbauen.
        base.Apply(context, symbol, result);
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer BufferStop hier einbauen.
        base.Release(context, symbol, result);
    }
}
