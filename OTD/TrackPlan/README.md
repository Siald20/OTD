# TrackPlan

Diese Datei dokumentiert den gesamten Ordner `TrackPlan`: Datenmodell, Graph/Route-Suche, Persistenz, Editor-Modell und Interlocking.

## Tabellenuebersicht

| Bereich | Datei/Klasse | Zweck | Wichtige API |
|---|---|---|---|
| Datenmodell | `TrackPlanDocument` | Persistierbares Editor-Dokument mit Symbolen/Verbindungen. | `Symbols`, `Connections` |
| Datenmodell | `TrackSymbol` | Laufzeit-Symbol fuer Suche/Interlocking. | `Properties`, `SwitchRouteOptions` |
| Datenmodell | `TrackConnection` | Laufzeit-Verbindung zwischen Symbolen. | `Reverse()` |
| Enums | `TrackSymbolKind`, `SignalDirection`, `SwitchPosition`, `RouteSearchMode` | Typisierung von Symbolen, Richtungen, Weichenlagen, Suchmodus. | Enum-Werte |
| Graph | `TrackPlanGraph` | In-Memory-Graph fuer Routing. | `AddSymbol`, `AddConnection`, `GetConnections` |
| Graph | `TrackPlanGraphFactory` | Baut Graph aus Dokument. | `CreateGraph(document)` |
| Suche | `RouteBuilder` | Findet moegliche Routen/Fahrstrassen. | `FindAllRoutes`, `FindRoute` |
| Suchregeln | `IRouteRule` / `DefaultRouteRule` | Steuerung erlaubter Symbole/Kanten und Zusatzkosten. | `CanUseSymbol`, `CanUseConnection`, `GetAdditionalCost` |
| Suchoptionen | `RouteSearchOptions` | Parameter fuer die Routenberechnung. | `SearchMode`, `AllowOccupiedSymbols`, `Rule` |
| Suchergebnis | `RouteResult` / `RouteSearchResult` | Struktur fuer gefundene Route oder Fehlermeldung. | `Success`, `Failed`, `SwitchCommands` |
| Editor | `TrackPlanEditorModel` | Bearbeitung des Dokuments auf Model-Ebene. | `AddSymbol`, `Connect`, `ToGraph` |
| Persistenz | `TrackPlanDocumentStore` | Laden/Speichern Gleisplan. | `Load`, `Save` |
| Persistenz | `RouteDocumentStore` | Laden/Speichern Routen. | `Load`, `Save` |
| Interlocking | `Interlocking/*` | Betriebliche Pruefung/Stellen/Aufloesen nach Suchergebnis. | `TrySetRoute`, `ReleaseRoute`, `TryApplyRoute` |

## Gesamtfluss

1. `TrackPlanDocument` beschreibt den gezeichneten Plan (Symbole + Verbindungen).
2. `TrackPlanGraphFactory` baut daraus einen laufzeitoptimierten `TrackPlanGraph`.
3. `RouteBuilder` sucht im Graphen moegliche Fahrstrassen (`RouteResult`).
4. `Interlocking` prueft/stellt eine gewaehlte Route betrieblich.

## Ordnerstruktur

- Root (`TrackPlan/*.cs`): Modell, Suche, Stores, Editor.
- `Interlocking/`: Stellwerkslogik.
  - [Interlocking README](C:/Users/saemi/RiderProjects/OTD/OTD/TrackPlan/Interlocking/README.md)
  - [Elements README](C:/Users/saemi/RiderProjects/OTD/OTD/TrackPlan/Interlocking/Elements/README.md)
  - [Profiles README](C:/Users/saemi/RiderProjects/OTD/OTD/TrackPlan/Interlocking/Profiles/README.md)

## Kernmodule (Root)

## Datenmodell

### `TrackPlanDocument` (`TrackPlanDocument.cs`)

- Persistierbares Editor-Dokument.
- Enthaelt:
  - `List<DrawnTrackSymbol> Symbols`
  - `List<DrawnTrackConnection> Connections`
- `DrawnTrackSymbol` traegt Position, Typ, Weichenlage, Optionen, Properties.
- `DrawnTrackConnection` traegt Kanteninfos inkl. Richtung/Bidirektionalitaet.

### `TrackSymbol`, `TrackConnection` (`TrackSymbol.cs`, `TrackConnection.cs`)

- Laufzeittypen fuer Suche/Interlocking.
- `TrackConnection.Reverse()` erzeugt die Gegenkante.
- `SwitchRouteOption.Matches(...)` prueft Port-Kombinationen fuer Weichenwege.

### `TrackSymbolKind` + Enums (`TrackSymbolKind.cs`)

- `TrackSymbolKind`: alle Elementtypen.
- `SignalDirection`, `SwitchPosition`, `RouteSearchMode`.

## Graph und Suche

### `TrackPlanGraph` (`TrackPlanGraph.cs`)

- In-Memory-Graph fuer Routing.
- API:
  - `AddSymbol(...)`
  - `AddConnection(...)`
  - `ConnectBidirectional(...)`
  - `GetSymbol(...)`, `TryGetSymbol(...)`
  - `GetConnections(symbolId)`

### `TrackPlanGraphFactory` (`TrackPlanGraphFactory.cs`)

- `CreateGraph(TrackPlanDocument document)`: transformiert Dokument -> Graph.

### `RouteBuilder` (`RouteBuilder.cs`)

- zentrale Routenberechnung.
- API:
  - `FindAllRoutes(...)` (Overloads)
  - `FindRoute(...)` (liefert `RouteSearchResult`)
  - `TryGetRequiredSwitchPosition(...)`
- Nutzt `RouteSearchOptions` und `IRouteRule`.

### `IRouteRule` / `DefaultRouteRule` (`IRouteRule.cs`)

- Erweiterungspunkt fuer Suchregeln.
- Methoden:
  - `CanUseSymbol(...)`
  - `CanUseConnection(...)`
  - `GetAdditionalCost(...)`
- Hilfsmethoden fuer Sicht-/Richtungskriterien:
  - `SignalAllowsDeparture(...)`
  - `SignalIsVisibleFrom(...)`
  - `BlockIsVisibleFrom(...)`

### `RouteSearchOptions` (`RouteSearchOptions.cs`)

- Suchparameter:
  - `SearchMode`
  - `AllowOccupiedSymbols`
  - `AllowLockedSymbols`
  - `MaxVisitedSymbols`
  - `Rule`

### `RouteResult` + `RouteSearchResult` (`RouteResult.cs`)

- `RouteResult`: gefundene Route (Start/Ziel, Symbole, Verbindungen, Weichenbefehle, Kosten).
- `RouteSearchResult`: Erfolg/Fehlschlag-Wrapper (`Success(...)`, `Failed(...)`).
- `SwitchCommand`: Weichenanweisung je Route.

## Editor und Persistenz

### `TrackPlanEditorModel` (`TrackPlanEditorModel.cs`)

- Manipulation des Dokuments auf Model-Ebene.
- API:
  - `AddSymbol(...)`
  - `Connect(...)`
  - `ToGraph()`

### `TrackPlanDocumentStore` (`TrackPlanDocumentStore.cs`)

- Laden/Speichern des Gleisplans:
  - `Load(filePath)`
  - `Save(filePath, document)`

### `RouteDocumentStore` (`RouteDocumentStore.cs`)

- Laden/Speichern von Routenlisten:
  - `Load(filePath, graph)`
  - `Save(filePath, routes)`

## Interlocking (Kurz)

Der Unterordner `Interlocking` ist die betriebliche Schicht nach der Suche:

- prueft Route (`Validate`),
- erzeugt Stellwirkungen (`Apply`),
- loest aktive Routen auf (`Release`).

Details stehen in:

- [Interlocking README](C:/Users/saemi/RiderProjects/OTD/OTD/TrackPlan/Interlocking/README.md)
- [Elements README](C:/Users/saemi/RiderProjects/OTD/OTD/TrackPlan/Interlocking/Elements/README.md)
- [Profiles README](C:/Users/saemi/RiderProjects/OTD/OTD/TrackPlan/Interlocking/Profiles/README.md)
