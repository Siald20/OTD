# Route Layout Schema

Diese Notiz beschreibt die aktuelle Zielarchitektur fuer RouteControl mit klarer Trennung zwischen
Topologie (`railwaylayout.xml`), Segment-Defaults (`topology.xml`) und zur Laufzeit komponierten `RouteLeg`s.

## 1. Rollen der Datenquellen

- `railwaylayout.xml` ist die primaere Quelle fuer:
   - Topologie (`trackelements`/`path`)
   - Rueckmeldelogik (`occupancy`, `contact`)
   - Waypoints (`waypoints`)
- `topology.xml` ist die sekundaere Quelle fuer **Nachbarsegment-Defaults** und fasst die Segmente unter `<segments>`:
  - `speedlimits`

`topology.xml` enthaelt **keine** Geometrie (`distance_cm`) und **keine** Rueckmeldelisten.
`topology.xml` enthaelt fuer die Segmentlogik nur `speedlimits`; andere Elemente wie `driveprofile`, `stoppoint` oder `acceleration_start_policy` werden ignoriert.
Legacy-Konfigurationen mit Root `<routesegments>` oder `<routelegs>` werden weiterhin toleriert.

## 2. XML-Schema (kompakt)

### `railwaylayout.xml`

```xml
<railwaylayout version="2">
    <trackelements>
        <trackelement id="seg_A13_W1" type="simpletrack" from="nA13" to="nW1" length_cm="160"/>
        <trackelement id="SW1" type="turnout">
            <path id="sw1_straight" from="nW1" to="nB120" length_cm="150"/>
            <path id="sw1_branch" from="nW1" to="nB121" length_cm="175"/>
        </trackelement>
    </trackelements>

    <feedbacks>
        <occupancy id="sec_A13" detectorId="30" host="seg_A13_W1"/>
        <contact id="contact_145" detectorId="145" host="seg_A13_W1" offset_cm="145"/>
    </feedbacks>

    <waypoints>
        <waypoint id="A13" node="nA13"/>
        <waypoint id="W1" node="nW1"/>
        <waypoint id="B120" node="nB120"/>
        <waypoint id="B121" node="nB121"/>
    </waypoints>
</railwaylayout>
```

### `topology.xml`

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

## 3. RouteLeg-Contract (oeffentlich vs. abgeleitet)

Zwischen Layout/Segments und RouteLeg liegt jetzt ein eigenes Domainmodell `RouteSegment` (`RouteSegment.cs`):

- `FromWaypointId`
- `ToWaypointId`
- `LengthCm` (readonly, aus Topologie abgeleitet)
- `MaxSpeedByClassKmh` (Geschwindigkeitsklassen)

`RouteLegResolver` bildet daraus RouteLegs.

### Oeffentlich setzbar bei `new RouteLeg(...)`

- `FromWaypointId`
- `ToWaypointId`
- `MaxSpeedKmh`
- `DriveProfile`
- `StopPoint`
- `Metadata`
- optional `AccelerationStartPolicy`

Diese Parameter beziehen sich auf die gesamte aufgeloeste RouteLeg-Distanz (ggf. ueber mehrere Segmente).

### Nicht oeffentlich setzbar (werden automatisch komponiert)

- `DistanceCm`
- `FeedbackActivationPoints`
- `FeedbackReferences`

Diese Felder duerfen nicht manuell ueberschrieben werden.

## 4. Kompositionslogik

`RouteLegResolver` bildet aus `FromWaypointId -> ToWaypointId` die Segmentkette ueber die Layout-Topologie.

Dabei gilt:

- `DistanceCm` = Summe der Segmentdistanzen
- `FeedbackActivationPoints` = aggregiert aus allen Segmenten (Offsets entlang der Gesamtdistanz)
- `FeedbackReferences` = aggregiert aus allen Segmenten (Offsets entlang der Gesamtdistanz)
- effektive Geschwindigkeit pro Leg:
  - `effectiveMaxSpeedKmh = min(RouteLeg.MaxSpeedKmh, Segment-Speedlimits entlang der Kette)`

## 5. Validierungsregeln

- Ein `segment` (legacy: `routesegment`/`routeleg`) muss einem benachbarten, auto-generierten Leg im Layout entsprechen.
- Segmente sind richtungsunabhaengig auf Segmentebene (`A<->B`), die konkrete Fahrtrichtung ergibt sich aus dem aufgeloesten Pfad.
- `intermediate` ist auf Segmentebene nicht erlaubt.
- Nicht passende Eintraege fuehren beim Laden zu `RouteValidationException`.

## 6. Runtime-Wiring

```csharp
var trackLayout = new XmlRailwayLayoutService();
var routeDefinitions = new XmlRouteDefinitionService(trackLayout);

var controller = new RouteController(
    train,
    routeDefinitions,
    trackLayout,
    initialHold: false);

controller.AddRoute(new RouteLeg(
    FromWaypointId: "A13",
    ToWaypointId: "B120",
    MaxSpeedKmh: 45));
```

`RouteController` und `RouteTableBuilder` nehmen schlanke RouteLeg-Inputs an und sorgen fuer die topologische Aufloesung vor dem Fahrbetrieb.
