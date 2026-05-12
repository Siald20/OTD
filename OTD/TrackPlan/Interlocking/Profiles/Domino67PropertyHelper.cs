using System;

namespace OTD.TrackPlan.Interlocking.Profiles;

public static class Domino67PropertyHelper
{
    /// <summary>
    /// Liest eine boolesche Property aus einem statischen Routensymbol.
    /// </summary>
    public static bool IsEnabled(TrackSymbol symbol, string propertyName)
    {
        return symbol.Properties.TryGetValue(propertyName, out var value) &&
               IsEnabledValue(value);
    }

    /// <summary>
    /// Liest eine boolesche Property aus einem gezeichneten Symbol im aktuellen Dokumentzustand.
    /// </summary>
    public static bool IsEnabled(DrawnTrackSymbol symbol, string propertyName)
    {
        return symbol.Properties.TryGetValue(propertyName, out var value) &&
               IsEnabledValue(value);
    }

    /// <summary>
    /// Liest den rohen Propertywert als String aus einem statischen Routensymbol.
    /// </summary>
    public static string? Get(TrackSymbol symbol, string propertyName)
    {
        return symbol.Properties.TryGetValue(propertyName, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Einheitliche Umwandlung eines Stringwerts in bool (`true`/`1`/`yes`).
    /// </summary>
    private static bool IsEnabledValue(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
