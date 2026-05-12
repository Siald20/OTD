namespace OTD.TrackPlan.Interlocking.Elements;

/// <summary>
/// Gemeinsame Basis fuer einfache Elementlogiken.
/// Die Basisklasse macht zwei Dinge:
/// 1. Sie bietet eine gemeinsame Demo-Sperre fuer jedes Element.
/// 2. Sie verriegelt ein Element standardmaessig, sobald die Fahrstrasse gestellt wird.
///
/// Spezifische Elementklassen ueberschreiben Validate und Apply nur dort,
/// wo sie wirklich zusaetzliche Regeln oder Stellwirkungen brauchen.
/// </summary>
public abstract class DefaultElementInterlockingLogic : IInterlockingElementLogic
{
    // Universeller Demo-Schalter. Wenn diese Property am Element auf true steht,
    // lehnt jede davon abgeleitete Elementlogik die Fahrstrasse ab.
    protected const string DemoBlockedProperty = "Demo.Blocked";

    protected DefaultElementInterlockingLogic(TrackSymbolKind kind)
    {
        Kind = kind;
    }

    public TrackSymbolKind Kind { get; }

    public virtual RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        return ValidateDemoBlocked(symbol, Kind.ToString());
    }

    public virtual void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // Default-Annahme: Jedes Element im Fahrweg wird durch die Fahrstrasse verriegelt.
        // Der UI-Code nutzt LockedSymbolIds aktuell noch nicht, aber die Information ist
        // bewusst im Resultat enthalten, damit echte Verschlusslogik spaeter anschliessen kann.
        result.LockSymbol(symbol.Id);
    }

    public virtual void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        _ = context;
        _ = symbol;
        _ = result;
    }

    protected static RouteSettingFailure? ValidateDemoBlocked(TrackSymbol symbol, string elementName)
    {
        return IsPropertyEnabled(symbol, DemoBlockedProperty)
            ? new RouteSettingFailure { Message = $"{symbol.Name} ist durch Demo-Logik gesperrt ({elementName})." }
            : null;
    }

    protected static RouteSettingFailure? ValidateRequiredProperty(
        TrackSymbol symbol,
        string propertyName,
        string failureMessage)
    {
        // Demo-Properties sind optional. Ist eine Property nicht gesetzt, soll sie den
        // Normalbetrieb nicht beeinflussen. Erst wenn sie explizit vorhanden ist, wird
        // ihr Wert ausgewertet.
        if (!symbol.Properties.ContainsKey(propertyName))
        {
            return null;
        }

        return IsPropertyEnabled(symbol, propertyName)
            ? null
            : new RouteSettingFailure { Message = failureMessage };
    }

    protected static bool IsPropertyEnabled(TrackSymbol symbol, string propertyName)
    {
        return symbol.Properties.TryGetValue(propertyName, out var value) &&
               (string.Equals(value, "true", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "1", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", System.StringComparison.OrdinalIgnoreCase));
    }

    protected static string? GetProperty(TrackSymbol symbol, string propertyName)
    {
        return symbol.Properties.TryGetValue(propertyName, out var value)
            ? value
            : null;
    }
}
