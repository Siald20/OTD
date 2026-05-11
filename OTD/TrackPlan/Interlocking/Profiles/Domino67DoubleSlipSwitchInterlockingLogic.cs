using OTD.TrackPlan.Interlocking.Elements;

namespace OTD.TrackPlan.Interlocking.Profiles;

public sealed class Domino67DoubleSlipSwitchInterlockingLogic : DoubleSlipSwitchInterlockingLogic
{
    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        var failure = base.Validate(context, symbol);
        if (failure is not null)
        {
            return failure;
        }

        if (!context.TryGetSwitchCommand(symbol.Id, out var command))
        {
            return new RouteSettingFailure { Message = $"{symbol.Name} hat keine Domino-67-DKW-Anforderung." };
        }

        if (Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.LocalControl))
        {
            return new RouteSettingFailure { Message = $"{symbol.Name} ist im Ortsbetrieb." };
        }

        if (Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.Locked) &&
            symbol.CurrentSwitchPosition != command.Position)
        {
            return new RouteSettingFailure { Message = $"{symbol.Name} ist verschlossen und kann nicht umgestellt werden." };
        }

        return null;
    }
}
