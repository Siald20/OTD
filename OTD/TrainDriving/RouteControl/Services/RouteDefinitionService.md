# Route Definition Service

## Ziel

`RouteController` kann weiterhin ueber `AddRoute(...)`, `AddRoutes(...)` und `ReplaceRoutes(...)` gesteuert werden.
Der dynamische API-Pfad uebergibt nur noch veraenderliche Betriebsdaten. Die statische Streckentopologie, die Leg-Distanzen und die Rueckmeldepositionen kommen jetzt **primaer aus `railwaylayout.xml`**.
`topology.xml` ist die Standardquelle fuer **Nachbarsegment-Defaults** (z. B. Geschwindigkeit, DriveProfile, StopPoint). Aktuelle Dateien fassen die einzelnen `<segment>`-Eintraege unter `<segments>` zusammen; Legacy-Dateien mit Root `<routesegments>` oder `<routelegs>` werden weiterhin gelesen.

## Architekturueberblick

### `Model Railway Layout` (`railwaylayout.xml`, primaere Quelle)

Das Layout beschreibt:
- befahrbare Topologie (`trackelements` mit `trackelement`, `switch/path`, optional `crossing/path`)
- physische Rueckmelder (`occupancy`, `contact`)
- logische Wegpunkte (`waypoints`)

Aus diesen Daten generiert `XmlRailwayLayoutService` automatisch gerichtete Legs zwischen benachbarten Waypoints.

### `topology.xml` (sekundaere Segment-Datei)

`topology.xml` enthaelt optionale Default-/Betriebsdaten fuer **benachbarte** Waypoint-Segmente, gruppiert unter `<segments>`:
- `speedlimits`

Es werden **keine** Distanzen, Rueckmelder, `driveprofile`, `stoppoint`, `acceleration_start_policy` oder richtungsspezifischen `<direction>`-Elemente definiert.

## XML-Schema (`topology.xml`)

```xml
<topology>
    <segments>
        <segment from="A13" to="W1">
            <speedlimits>
                <speedlimit speedClass="default" speed_kmh="60"/>
            </speedlimits>
        </segment>

        <segment from="W1" to="B120">
            <speedlimits>
                <speedlimit speedClass="default" speed_kmh="40"/>
            </speedlimits>
        </segment>
    </segments>
</topology>
```

## Aufloesungslogik

1. `XmlRailwayLayoutService` laedt `railwaylayout.xml`
2. aus Waypoints + Topologie werden gerichtete `GeneratedTrackLeg`-Eintraege erzeugt
3. Rueckmelder werden dabei automatisch auf `FeedbackActivationPoint` abgebildet:
   - `occupancy` (`FeedbackType.OccupancyFeedback`) → Offset = Einfahrt in den Host-Track relativ zur Fahrtrichtung
   - `contact` (`FeedbackType.ContactFeedback`) → Offset = fixer Punkt auf dem Host-Track
4. `XmlRouteDefinitionService` laedt standardmaessig `topology.xml` (richtungslos je Nachbarpaar, legacy: `<routesegments>`/`<routelegs>`)
5. `RouteLegResolver` ermittelt die Segmentkette `FromWaypointId -> ToWaypointId` auf Basis von `RouteSegment`
6. `DistanceCm` kommt ausschliesslich aus `RouteSegment.LengthCm` (topologisch abgeleitet), Rueckmeldungen/Feedback werden aggregiert

## Regeln fuer automatische Rueckmeldepositionen

- `occupancy` liegt nicht an einem festen absoluten Punkt, sondern an der **Einfahrtsseite des Host-Tracks**.
- Beim Richtungswechsel aendert sich daher der berechnete Offset automatisch.
- `contact` bleibt ein fester geometrischer Punkt auf dem Host-Track.
- Waypoints muessen das Layout so partitionieren, dass zwischen zwei benachbarten Waypoints ein eindeutiger Leg entsteht.

## Integration

```csharp
var trackLayout = new XmlRailwayLayoutService();
var routeDefinitions = new XmlRouteDefinitionService(trackLayout);
var controller = new RouteController(train, routeDefinitions, trackLayout, initialHold: true);

controller.AddRoute(new RouteLeg(
    FromWaypointId: "A13",
    ToWaypointId: "W1",
    MaxSpeedKmh: 45));
```

## Aufloesungsregeln (API-Ebene)

- `DistanceCm`: automatisch aus der ermittelten Segmentkette
- `FeedbackActivationPoints`: automatisch aggregiert aus der Segmentkette
- `FeedbackReferences`: automatisch aggregiert aus der Segmentkette
- `MaxSpeedKmh`: `min(RouteLeg.MaxSpeedKmh, segmentSpeedLimit)` entlang der gesamten Kette
- `DriveProfile`: ausschliesslich `RouteLeg`-Wert
- `StopPoint`: ausschliesslich `RouteLeg`-Wert
- `AccelerationStartPolicy`: `RouteLeg`-Wert (Fallback: `AfterTrainClearsWaypoint`)

## Hinweise und Validierung

- Ein `segment` (legacy: `routesegment`/`routeleg`) muss auf ein bereits aus dem Layout generiertes **Nachbar-Leg** passen.
- `intermediate` ist auf Segmentebene nicht erlaubt.
- Segmentdefinitionen sind richtungsunabhaengig (`A->B` entspricht `B->A`).
- In `segment`/`routesegment` werden nur `speedlimits` ausgewertet; andere Elemente (z. B. `driveprofile`, `stoppoint`, `acceleration_start_policy`) werden ignoriert.
- Nicht passende Overrides fuehren beim Laden zu einer `RouteValidationException`.
- Richtungsspezifische Overrides via `<direction>` sind nicht mehr zulaessig.
- Wenn ein Layout-Zyklus keine zusaetzlichen Waypoints enthaelt, wird die Leg-Generierung mit Validierungsfehler abgebrochen.

### Helper: Vollstaendige Runde pruefen

```csharp
var routeDefinitions = new XmlRouteDefinitionService(trackLayout);
var roundCheck = RouteRoundtripHelper.ValidateCompleteRound(
    routeDefinitions,
    startWaypointId: "S_A13",
    requireDefaultSpeed: true,
    requireAllEligibleLegsUsed: true);

if (!roundCheck.IsCompleteRound)
    throw new InvalidOperationException(roundCheck.Message);
```

Die Helper-Methode meldet u. a.:
- fehlende Legs (`from='...'` ohne Folgeeintrag)
- mehrdeutige Abzweige (mehrere ausgehende Legs ab gleichem `from`)
- nicht geschlossene Kette
- zusaetzliche/ungenutzte Legs (wenn `requireAllEligibleLegsUsed=true`)

## Weiterfuehrende Dokumente

- `../Layout/RailwayLayoutTopologySchema.md` – Benutzerorientierte Schema-Beschreibung fuer `railwaylayout.xml`
- `../RouteLayoutSchema.md` – kompakte Architektursicht fuer Entwickler
