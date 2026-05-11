namespace OTD.TrackPlan.Interlocking;

/// <summary>
/// Beschreibt die Stellwerkslogik fuer genau einen Elementtyp.
/// Die Fahrstrassensuche liefert nur einen moeglichen Weg; diese Logik entscheidet
/// erst beim Stellen, ob das einzelne Element benutzt werden darf und welche
/// Zustandsaenderungen dafuer noetig sind.
/// </summary>
public interface IInterlockingElementLogic
{
    /// <summary>
    /// Elementtyp, fuer den diese Logik registriert wird.
    /// </summary>
    TrackSymbolKind Kind { get; }

    /// <summary>
    /// Prueft vor dem Stellen, ob dieses Element die Fahrstrasse erlaubt.
    /// Hier gehoeren Abhaengigkeiten wie Belegung, Verschluss, technische Freigaben,
    /// Bahnuebergang geschlossen, Weiche in erlaubter Lage usw. hinein.
    /// </summary>
    RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol);

    /// <summary>
    /// Wendet die Stellwirkung an, nachdem alle Elemente erfolgreich validiert wurden.
    /// Diese Methode soll keine neuen Sperrgruende mehr suchen, sondern nur noch
    /// berechnete Aktionen ausfuehren: Weiche stellen, Signal freigeben, Symbol verriegeln.
    /// </summary>
    void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result);
}
