namespace OTD.TrackPlan.Interlocking.Profiles;

public sealed class Domino67ZwergSignalInterlockingLogic : Elements.DefaultElementInterlockingLogic
{
    public Domino67ZwergSignalInterlockingLogic()
        : base(TrackSymbolKind.ZwergSignal)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        var failure = base.Validate(context, symbol);
        if (failure is not null)
        {
            return failure;
        }

        return Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.TrackLocked)
            ? new RouteSettingFailure { Message = $"{symbol.Name} ist im Domino 67 gesperrt." }
            : null;
    }

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer Domino67-Zwergsignal hier erweitern.
        result.LockSymbol(symbol.Id);

        if (context.Request.RouteType is not RouteType.Shunting)
        {
            return;
        }

        if (symbol.Id == context.Request.Route.StartSignal.Id)
        {
            result.AddGreenSignal(symbol.Id);
        }
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer Domino67-Zwergsignal hier einbauen.
        _ = result;
        context.SetSymbolProperty(symbol.Id, Domino67PropertyNames.SignalIsGreen, false);
    }
}
