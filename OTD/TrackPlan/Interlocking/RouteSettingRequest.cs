using System;
using System.Collections.Generic;

namespace OTD.TrackPlan.Interlocking;

/// <summary>
/// Eingangsdaten fuer einen Stellversuch.
/// Der Request trennt bewusst Suchergebnis, Dokumentzustand und Bedienzustand:
/// RouteResult kommt aus der Suche, Document ist der veraenderbare Gleisplan,
/// OccupiedSymbolIds und ActiveRoute kommen aus der Bedienebene.
/// </summary>
public sealed class RouteSettingRequest
{
    public RouteType RouteType { get; init; } = RouteType.Train;

    /// <summary>
    /// Die bereits gefundene Fahrstrasse, die jetzt gestellt werden soll.
    /// </summary>
    public required RouteResult Route { get; init; }

    /// <summary>
    /// Veraenderbarer Gleisplan. Apply-Logiken schreiben hier z. B. Weichenlagen zurueck.
    /// </summary>
    public required TrackPlanDocument Document { get; init; }

    /// <summary>
    /// Aktuell belegte Elemente. Das ist bewusst ausserhalb des Dokuments, damit
    /// Belegung spaeter aus Rueckmeldern, Simulation oder Netzwerk kommen kann.
    /// </summary>
    public IReadOnlySet<string> OccupiedSymbolIds { get; init; } = new HashSet<string>();

    /// <summary>
    /// Bereits aktive Fahrstrassen. Neue Fahrstrassen duerfen parallel gestellt werden,
    /// wenn das Profil keine Konflikte mit ihnen erkennt.
    /// </summary>
    public IReadOnlyList<RouteResult> ActiveRoutes { get; init; } = [];

    /// <summary>
    /// Durch aktive Fahrstrassen oder Schutzweichen verschlossene Symbole.
    /// </summary>
    public IReadOnlySet<string> LockedSymbolIds { get; init; } = new HashSet<string>();

    /// <summary>
    /// Optionaler Rueckkanal fuer asynchron ausgefuehrte Stellwirkungen (z. B. verzoegertes
    /// Schliessen eines Bahnuebergangs). Die Fachlogik bleibt im Interlocking-Service.
    /// </summary>
    public Action<RouteSettingContext, RouteSettingResultBuilder>? DelayedActionApplied { get; init; }
}
