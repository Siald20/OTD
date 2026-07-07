# RouteControl Layout Vorbereitung

Dieses Dokument hält die Trennung zwischen Layout-Topologie und betrieblicher Route-Definition im aktuellen Modell fest.

- `railwaylayout.xml` beschreibt die physische und geometrische Fahrwelt über den gemeinsamen Oberknoten `trackelements`.
- `railwaylayout.xml` enthält ausserdem die Sensorik und die Waypoints.
- Aus diesen Daten werden `RouteLeg`s automatisch generiert.
- `routelegs.xml` beschreibt nur noch betriebliche Overrides wie Geschwindigkeit, Fahrprofil oder Haltepunkt.

Die GUI folgt später auf derselben Layout-Datenbasis.
