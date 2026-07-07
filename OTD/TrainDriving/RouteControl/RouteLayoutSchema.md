# Route Layout Schema

Diese Notiz beschreibt die aktuelle Zielarchitektur fuer RouteControl mit klarer Trennung zwischen
Topologie (`railwaylayout.xml`), Segment-Defaults (`routesegments.xml`) und zur Laufzeit komponierten `RouteLeg`s.

## 1. Rollen der Datenquellen

- `railwaylayout.xml` ist die primaere Quelle fuer:
  - Topologie (`trackelements`/`path`)
  - Sensorik (`section`, `point`)
  - Waypoints (`waypoints`)
- `routesegments.xml` ist die sekundaere Quelle fuer **Nachbarsegment-Defaults**:
  - `speedlimits`

`routesegments.xml` enthaelt **keine** Geometrie (`distance_cm`) und **keine** Sensorlisten.
`routesegments.xml` enthaelt fuer die Segmentlogik nur `speedlimits`; andere Elemente wie `driveprofile`, `stoppoint` oder `acceleration_start_policy` werden ignoriert.

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

    <sensors>
        <section id="sec_A13" detectorId="30" host="seg_A13_W1"/>
        <point id="contact_145" detectorId="145" host="seg_A13_W1" offset_cm="145"/>
    </sensors>

    <waypoints>
        <waypoint id="A13" node="nA13"/>
        <waypoint id="W1" node="nW1"/>
        <waypoint id="B120" node="nB120"/>
        <waypoint id="B121" node="nB121"/>
    </waypoints>
</railwaylayout>
```

### `routesegments.xml`

```xml
<routesegments>
    <routesegment from="A13" to="W1">
        <speedlimits>
            <speedlimit speedClass="default" speed_kmh="60"/>
        </speedlimits>
    </routesegment>

    <routesegment from="W1" to="B120">
        <speedlimits>
            <speedlimit speedClass="default" speed_kmh="40"/>
        </speedlimits>
    </routesegment>
</routesegments>
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
- `SensorMarkers`
- `FeedbackReferences`

Diese Felder duerfen nicht manuell ueberschrieben werden.

## 4. Kompositionslogik

`RouteLegResolver` bildet aus `FromWaypointId -> ToWaypointId` die Segmentkette ueber die Layout-Topologie.

Dabei gilt:

- `DistanceCm` = Summe der Segmentdistanzen
- `SensorMarkers` = aggregiert aus allen Segmenten (Offsets entlang der Gesamtdistanz)
- `FeedbackReferences` = aggregiert aus allen Segmenten (Offsets entlang der Gesamtdistanz)
- effektive Geschwindigkeit pro Leg:
  - `effectiveMaxSpeedKmh = min(RouteLeg.MaxSpeedKmh, Segment-Speedlimits entlang der Kette)`

## 5. Validierungsregeln

- Ein `routesegment` muss einem benachbarten, auto-generierten Leg im Layout entsprechen.
- `routesegments` sind richtungsunabhaengig auf Segmentebene (`A<->B`), die konkrete Fahrtrichtung ergibt sich aus dem aufgeloesten Pfad.
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
