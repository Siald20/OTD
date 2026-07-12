# Tracklayout Topology Schema

## Ziel

`railwaylayout.xml` ist die geometrische und rueckmeldebezogene Primärquelle für RouteControl.
Die Datei beschreibt:
- die befahrbare Topologie,
- die Lage physischer Rueckmelder,
- und die logischen Waypoints, aus denen automatisch `RouteLeg`s entstehen.

`topology.xml` ergaenzt diese Daten um betriebliche Segment-Overrides. Legacy-Dateien mit Root `<routesegments>` oder `<routelegs>` werden weiterhin akzeptiert.

## Grundstruktur

```xml
<railwaylayout version="2">
    <trackelements>
        <trackelement id="seg_A13_W1" type="simpletrack" from="nA13" to="nW1" length_cm="160"/>
        <trackelement id="SW1" type="turnout">
            <path id="sw1_straight" from="nW1" to="nB120" length_cm="150"/>
            <path id="sw1_branch" from="nW1" to="nB121" length_cm="175"/>
        </trackelement>

        <trackelement id="X1" type="crossing">
            <path id="x1_ns" from="nNorth" to="nSouth" length_cm="120"/>
            <path id="x1_we" from="nWest" to="nEast" length_cm="120"/>
        </trackelement>
    </trackelements>

    <feedbacks>
        <occupancy id="sec_A13" detectorId="30" host="seg_A13_W1"/>
        <contact id="contact_145" detectorId="145" host="seg_A13_W1" offset_cm="145"/>
    </feedbacks>

    <waypoints>
        <waypoint id="A13" node="nA13"/>
        <waypoint id="W1" node="nW1"/>
    </waypoints>
</railwaylayout>
```

## Elemente

### 1. `trackelements`

Alle befahrbaren Gleiselemente liegen unter einem gemeinsamen Oberknoten.

- `trackelement type="simpletrack"`: lineares Gleis mit genau einem Fahrweg
- `trackelement type="turnout"`: Weiche mit mehreren `path`-Unterknoten
- `trackelement type="crossing"`: Kreuzung mit mehreren `path`-Unterknoten

### 1.1 `trackelement`

Normale Streckenabschnitte mit fester Länge.

Pflichtattribute pro `trackelement`:
- `id`
- `from`
- `to`
- `length_cm`

Beispiel:

```xml
<trackelement id="seg_A13_W1" from="nA13" to="nW1" length_cm="160"/>
```

### 1.2 `trackelement type="turnout"`

Eine Weiche enthält einen oder mehrere befahrbare `path`-Einträge.
Jeder `path` ist ein eigener befahrbarer geometrischer Zweig mit eigener Länge.
Das `trackelement`-Objekt mit `type="turnout"` definiert **keine** `from`/`to`/`length_cm` Attribute.

Pflichtattribute pro `path`:
- `id`
- `from`
- `to`
- `length_cm`

Beispiel:

```xml
<trackelement id="SW1" type="turnout">
    <path id="sw1_straight" from="nW1" to="nB120" length_cm="150"/>
    <path id="sw1_branch" from="nW1" to="nB121" length_cm="175"/>
</trackelement>
```

### 1.3 `trackelement type="crossing"`

Eine Kreuzung wird analog modelliert: jeder befahrbare Weg ist ein `path`.

```xml
<trackelement id="X1" type="crossing">
    <path id="x1_ns" from="nNorth" to="nSouth" length_cm="120"/>
    <path id="x1_we" from="nWest" to="nEast" length_cm="120"/>
</trackelement>
```

## 2. `feedbacks`

Es gibt zwei Feedback-Typen.

### `occupancy`

Ein `occupancy` repräsentiert einen **Belegtmelder** (Abschnittsrueckmelder).
Der Rueckmelder liegt logisch an der **Einfahrtsseite** des referenzierten Hosts.
`host` darf dabei entweder auf ein komplettes `trackelement` oder auf einen einzelnen `path` zeigen.
Bei mehrpfadigen Elementen wie `turnout` oder `crossing` wird ein `occupancy`-Rueckmelder auf allen Pfaden des referenzierten Oberelements berücksichtigt.

Pflichtattribute:
- `id`
- `detectorId`
- `host`

```xml
<occupancy id="sec_A13" detectorId="30" host="seg_A13_W1"/>
```

### `contact`

Ein `contact` repräsentiert einen **Punktkontakt** (Gleiskontakt an festem Offset).
Der Rueckmelder liegt an einem festen Offset auf dem referenzierten Host-Track.
Für `contact` ist weiterhin ein eindeutiger, einzelner Host-Track sinnvoll; bei mehrpfadigen Elementen sollte dafür ein konkreter `path` referenziert werden.

Pflichtattribute:
- `id`
- `detectorId`
- `host`
- `offset_cm`

```xml
<contact id="contact_145" detectorId="145" host="seg_A13_W1" offset_cm="145"/>
```

## 3. `waypoints`

Waypoints partitionieren die Topologie in automatisch generierte Legs.
Ein Waypoint liegt entweder:
- auf einem `node`, oder
- innerhalb eines Host-Tracks mit `host` + `offset_cm`

### Waypoint auf Node

```xml
<waypoint id="W1" node="nW1"/>
```

### Waypoint innerhalb eines Tracks

```xml
<waypoint id="MID_A13_W1" host="seg_A13_W1" offset_cm="60"/>
```

## 4. Automatisch erzeugte Legs

Zwischen zwei benachbarten Waypoints erzeugt `XmlRailwayLayoutService` automatisch ein gerichtetes Leg.
Dabei werden berechnet:
- `DistanceCm`
- `FeedbackActivationPoints`

### Rueckmelderegeln

- `occupancy` (`FeedbackType.OccupancyFeedback`) → Aktivierungspunkt bei Einfahrt in den Host-Track
- `contact` (`FeedbackType.ContactFeedback`) → Aktivierungspunkt am festen Offset auf dem Host-Track
- bei Rückfahrt wird kein eigener XML-Eintrag benötigt; die Richtung ergibt sich aus der Topologie

## 5. Validierungsregeln

- Jeder Track (`trackelement`, `path`) benötigt positive `length_cm`
- `from` und `to` eines Tracks müssen verschieden sein
- `host` eines Rueckmelders oder Waypoints muss auf einen existierenden Track oder Path zeigen
- `occupancy.host` darf auf ein `trackelement` oder einen `path` zeigen
- `contact.host` soll auf einen eindeutigen Track oder `path` zeigen
- `offset_cm` eines `contact` oder host-basierten Waypoints muss innerhalb `[0, length_cm]` liegen
- Ein `Waypoint` muss eindeutig sein
- Topologische Zyklen ohne zusätzlichen Waypoint werden abgewiesen

## 6. Betriebsdaten in `topology.xml`

Zu den automatisch generierten Legs können optionale Overrides definiert werden:

```xml
<topology>
    <segments>
        <segment from="A13" to="W1">
            <speedlimits>
                <speedlimit speedClass="default" speed_kmh="60"/>
            </speedlimits>
        </segment>
    </segments>
</topology>
```

`topology.xml` definiert dabei **keine** Rueckmelder und **keine** Distanz mehr.

