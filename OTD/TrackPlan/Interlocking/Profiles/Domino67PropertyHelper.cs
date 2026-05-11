using System;

namespace OTD.TrackPlan.Interlocking.Profiles;

public static class Domino67PropertyHelper
{
    public static bool IsEnabled(TrackSymbol symbol, string propertyName)
    {
        return symbol.Properties.TryGetValue(propertyName, out var value) &&
               (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));
    }

    public static string? Get(TrackSymbol symbol, string propertyName)
    {
        return symbol.Properties.TryGetValue(propertyName, out var value)
            ? value
            : null;
    }
}
