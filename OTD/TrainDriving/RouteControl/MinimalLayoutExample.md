# Minimal Example: `railwaylayout.xml` + `topology.xml`

Dieses Minimalbeispiel zeigt die kleinstmoegliche Konfiguration mit topologiebasiertem Layout und automatisch erzeugten Legs.

## 1) `railwaylayout.xml`

```xml
<railwaylayout version="2">
    <trackelements>
        <trackelement id="seg_A" type="simpletrack" from="nA" to="nW" length_cm="150"/>
        <trackelement id="SW1" type="turnout">
            <path id="sw1_main" from="nW" to="nB1" length_cm="95"/>
            <path id="sw1_branch" from="nW" to="nB2" length_cm="110"/>
        </trackelement>
    </trackelements>

    <feedbacks>
        <occupancy id="sec_A" detectorId="30" host="seg_A"/>
        <contact id="pt_145" detectorId="145" host="seg_A" offset_cm="145"/>
        <occupancy id="sec_branch" detectorId="32" host="sw1_branch"/>
    </feedbacks>

    <waypoints>
        <waypoint id="A" node="nA"/>
        <waypoint id="MID" host="seg_A" offset_cm="60"/>
        <waypoint id="W" node="nW"/>
        <waypoint id="B1" node="nB1"/>
        <waypoint id="B2" node="nB2"/>
    </waypoints>
</railwaylayout>
```

## 2) `topology.xml`

```xml
<topology>
    <segments>
        <segment from="A" to="MID">
            <speedlimits>
                <speedlimit speedClass="default" speed_kmh="40"/>
            </speedlimits>
        </segment>
        <segment from="MID" to="W">
            <speedlimits>
                <speedlimit speedClass="default" speed_kmh="50"/>
            </speedlimits>
        </segment>
        <segment from="W" to="B2">
            <speedlimits>
                <speedlimit speedClass="default" speed_kmh="30"/>
            </speedlimits>
        </segment>
    </segments>
</topology>
```

## 3) Runtime-Wiring

```csharp
var trackLayout = new XmlRailwayLayoutService();
var routeDefinitions = new XmlRouteDefinitionService(trackLayout);
var resolver = new RouteLegResolver(routeDefinitions);
```

## Hinweise

- `XmlRailwayLayoutService` erzeugt die gerichteten Legs direkt aus Waypoints und Topologie.
- `XmlRouteDefinitionService` legt nur betriebliche Overrides aus `topology.xml` darueber.
- `occupancy` (`FeedbackType.OccupancyFeedback`) wird automatisch an der Einfahrt in den jeweiligen Host-Track zugeordnet.
- `contact` (`FeedbackType.ContactFeedback`) bleibt ein fixer Punkt mit geometrischem `offset_cm`.

## Ausfuehrung (ohne Hardware)

Im Test-Menue ist ein hardwarefreier Eintrag vorhanden:

- `TRAINDRIVING_ROUTE_LAYOUT_MINIMAL`

Direktstart:

```bash
cd /home/hua/RiderProjects/OTD/OTD
OTD_ENTRYPOINT=TEST_HARDWARECONTROL OTD_TEST_CASE=TRAINDRIVING_ROUTE_LAYOUT_MINIMAL dotnet run
```

## Beispielausgabe

```text
[Minimal] Automatisch generierte Legs:
  A->MID, dist=60cm, feedbacks=[30@0cm/OccupancyFeedback]
  MID->W, dist=90cm, feedbacks=[145@85cm/ContactFeedback]
  W->MID, dist=90cm, feedbacks=[30@0cm/OccupancyFeedback, 145@5cm/ContactFeedback]
  W->B2, dist=110cm, feedbacks=[32@0cm/OccupancyFeedback]
[Minimal] Alle Assertions erfolgreich.
```
