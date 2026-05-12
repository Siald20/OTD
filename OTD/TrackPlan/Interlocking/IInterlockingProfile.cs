namespace OTD.TrackPlan.Interlocking;

public interface IInterlockingProfile
{
    string Name { get; }

    /// <summary>
    /// Globale Vorpruefung fuer eine Fahrstrasse, bevor einzelne Elemente validiert werden.
    /// Hier gehoeren profilweite Abhaengigkeiten hin: feindliche Fahrstrassen,
    /// Start-/Zielbedingungen, Flankenschutzlisten oder Bedienverbote.
    /// </summary>
    RouteSettingFailure? ValidateRoute(RouteSettingContext context);

    /// <summary>
    /// Profilweite Stellwirkungen nach erfolgreicher Element-Apply-Phase.
    /// Wird genutzt fuer automatisch berechnete Zusatzverschluesse wie Flankenschutz.
    /// </summary>
    void ApplyRoute(RouteSettingContext context, RouteSettingResultBuilder result);

    /// <summary>
    /// Profilweite Aufloesewirkungen beim Aufheben einer aktiven Fahrstrasse.
    /// </summary>
    void ReleaseRoute(RouteSettingContext context, RouteSettingResultBuilder result);

    IInterlockingElementLogic GetLogic(TrackSymbolKind kind);
}
