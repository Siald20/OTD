using System;
using System.Globalization;
using OTD.TrackPlan.Interlocking.Elements;

namespace OTD.TrackPlan.Interlocking.Profiles;

public sealed class Domino67LevelCrossingInterlockingLogic : LevelCrossingInterlockingLogic
{
    private const int DefaultAutoCloseDelaySeconds = 10;

    /// <summary>
    /// Erlaubt die Fahrstrasse nur, wenn der Bahnuebergang bereits geschlossen ist
    /// oder fuer Auto-Close konfiguriert wurde.
    /// </summary>
    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        if (Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.LevelCrossingClosed) ||
            Domino67PropertyHelper.IsEnabled(symbol, Domino67PropertyNames.LevelCrossingAutoClose))
        {
            return null;
        }

        return new RouteSettingFailure { Message = $"{symbol.Name} ist im Domino 67 nicht geschlossen." };
    }

    /// <summary>
    /// Die konkrete Schliessung erfolgt ueber die Startsignal-Logik, damit die
    /// Reihenfolge "BUE zu, dann Signal gruen" an einer Stelle gesteuert wird.
    /// </summary>
    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        _ = context;
        _ = symbol;
        _ = result;
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        _ = result;
        var currentCrossing = context.FindDrawnSymbol(symbol.Id);
        var keepClosed = currentCrossing is not null &&
                         Domino67PropertyHelper.IsEnabled(currentCrossing, Domino67PropertyNames.LevelCrossingKeepClosedOnRelease);

        if (!keepClosed)
        {
            context.SetSymbolProperty(symbol.Id, Domino67PropertyNames.LevelCrossingClosed, false);
        }
    }

    public static TimeSpan GetAutoCloseDelay(TrackSymbol levelCrossing)
    {
        if (levelCrossing.Properties.TryGetValue(Domino67PropertyNames.LevelCrossingAutoCloseDelaySeconds, out var rawDelay) &&
            int.TryParse(rawDelay, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) &&
            seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.FromSeconds(DefaultAutoCloseDelaySeconds);
    }
}
