# RouteControl Layout Vorbereitung

Dieses Dokument hält die Trennung zwischen Layout-Topologie und betrieblicher Route-Definition im aktuellen Modell fest.

- `railwaylayout.xml` beschreibt die physische und geometrische Fahrwelt über den gemeinsamen Oberknoten `trackelements`.
- `railwaylayout.xml` enthaelt ausserdem Rueckmeldelogik (`feedbacks`) und die Waypoints.
- Aus diesen Daten werden `RouteLeg`s automatisch generiert.
- `topology.xml` beschreibt betriebliche Segment-Overrides wie Geschwindigkeiten.
- Legacy-Roots `<routesegments>` und `<routelegs>` werden weiterhin toleriert.

Die GUI folgt später auf derselben Layout-Datenbasis.
