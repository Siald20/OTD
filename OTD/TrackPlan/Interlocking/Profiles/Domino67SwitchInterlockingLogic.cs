using OTD.TrackPlan.Interlocking.Elements;

namespace OTD.TrackPlan.Interlocking.Profiles;

public sealed class Domino67SwitchInterlockingLogic : SwitchInterlockingLogic
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
            return new RouteSettingFailure { Message = $"{symbol.Name} hat keine Domino-67-Weichenanforderung." };
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

        var requiredPosition = Domino67PropertyHelper.Get(symbol, Domino67PropertyNames.RequiredPosition);
        if (!string.IsNullOrWhiteSpace(requiredPosition) &&
            !string.Equals(requiredPosition, command.Position.ToString(), System.StringComparison.OrdinalIgnoreCase))
        {
            return new RouteSettingFailure { Message = $"{symbol.Name} muss Domino-67-Lage {requiredPosition} behalten." };
        }

        return null;
    }
}
