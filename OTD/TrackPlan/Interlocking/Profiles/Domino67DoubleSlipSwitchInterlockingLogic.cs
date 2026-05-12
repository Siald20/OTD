using OTD.TrackPlan.Interlocking.Elements;
using System;

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

        if (Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.SwitchLocked))
        {
            return new RouteSettingFailure { Message = $"{symbol.Name} Weiche ist verschlossen." };
        }

        if (Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.SwitchLocked) &&
            symbol.CurrentSwitchPosition != command.Position)
        {
            return new RouteSettingFailure { Message = $"{symbol.Name} ist verschlossen und kann nicht umgestellt werden." };
        }

        return null;
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        var currentSwitch = context.FindDrawnSymbol(symbol.Id);
        if (currentSwitch is null ||
            !Domino67PropertyHelper.IsEnabled(currentSwitch, Domino67PropertyNames.SwitchReleaseToRequiredPosition))
        {
            return;
        }

        var requiredPosition = Domino67PropertyHelper.Get(symbol, Domino67PropertyNames.RequiredPosition);
        if (!Enum.TryParse<SwitchPosition>(requiredPosition, true, out var required))
        {
            return;
        }

        currentSwitch.CurrentSwitchPosition = required;
        result.AddSwitchCommand(new SwitchCommand
        {
            SwitchId = symbol.Id,
            SwitchName = symbol.Name,
            Position = required
        });
    }
}
