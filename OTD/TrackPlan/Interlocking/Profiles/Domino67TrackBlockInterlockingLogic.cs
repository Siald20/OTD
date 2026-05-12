using OTD.TrackPlan.Interlocking.Elements;

namespace OTD.TrackPlan.Interlocking.Profiles;

public sealed class Domino67TrackBlockInterlockingLogic : TrackBlockInterlockingLogic
{
    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        var failure = base.Validate(context, symbol);
        if (failure is not null)
        {
            return failure;
        }

        if (Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.TrackClosed))
        {
            return new RouteSettingFailure { Message = $"{symbol.Name} ist im Domino 67 gesperrt." };
        }

        var overlapSymbols = context.ParseSymbolList(symbol, Domino67PropertyNames.OverlapSymbols);
        return context.AnySymbolOccupied(overlapSymbols)
            ? new RouteSettingFailure { Message = $"{symbol.Name}: Durchrutschweg ist belegt." }
            : null;
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        _ = context;
        _ = symbol;
        _ = result;
    }
}
