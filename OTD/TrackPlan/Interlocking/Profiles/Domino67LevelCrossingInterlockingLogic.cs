using OTD.TrackPlan.Interlocking.Elements;

namespace OTD.TrackPlan.Interlocking.Profiles;

public sealed class Domino67LevelCrossingInterlockingLogic : LevelCrossingInterlockingLogic
{
    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        var failure = base.Validate(context, symbol);
        if (failure is not null)
        {
            return failure;
        }

        if (Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.LevelCrossingClosed) ||
            Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.LevelCrossingAutoClose))
        {
            return null;
        }

        return new RouteSettingFailure { Message = $"{symbol.Name} ist im Domino 67 nicht geschlossen." };
    }
}
