# RouteControl Docs

Diese Uebersicht haelt die RouteControl-Dokumentation nahe am Code.

## Kern-Dokumente

- `RouteControl_SPEC_v1.md`
  - Fachlicher API-/Laufzeitvertrag und Sicherheitsregeln.
- `RouteLayoutSchema.md`
  - Kompakte Architektur- und Datenflusssicht.
- `MinimalLayoutExample.md`
  - Kleines End-to-End-Konfigurationsbeispiel.

## Kontextbezogene Unterordner

- `Layout/RailwayLayoutTopologySchema.md`
  - Struktur von `railwaylayout.xml` und Segment-Overrides in `topology.xml`.
- `Services/RouteDefinitionService.md`
  - Verhalten von `XmlRouteDefinitionService` und Segmentauflösung.

## Deep Dive / Analyse

- `DocumentationSource.md`
  - Ausführliche Analyse zur Platzierung von `FeedbackActivationPoint` an Leg-Uebergaengen.

## Hinweis zu Legacy-XML

Die Laufzeit bevorzugt `topology.xml` fuer Segment-Defaults.
Legacy-Roots `<routesegments>` und `<routelegs>` werden weiterhin gelesen.

