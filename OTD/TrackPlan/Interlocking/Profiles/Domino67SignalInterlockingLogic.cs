namespace OTD.TrackPlan.Interlocking.Profiles;

public sealed class Domino67SignalInterlockingLogic : Elements.DefaultElementInterlockingLogic
{
    public Domino67SignalInterlockingLogic()
        : base(TrackSymbolKind.Signal)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        var failure = base.Validate(context, symbol);
        if (failure is not null)
        {
            return failure;
        }

        return Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.Blocked)
            ? new RouteSettingFailure { Message = $"{symbol.Name} ist im Domino 67 gesperrt." }
            : null;
    }

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        result.LockSymbol(symbol.Id);

        if (symbol.Id == context.Request.Route.StartSignal.Id &&
            !Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.HoldRed))
        {
            result.AddGreenSignal(symbol.Id);
        }
    }
}
