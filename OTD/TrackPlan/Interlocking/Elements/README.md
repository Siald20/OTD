# Interlocking Elements

Diese Datei dokumentiert die Elementlogiken im Ordner `Interlocking/Elements`.

## Grundprinzip

Alle Klassen implementieren (direkt oder indirekt) `IInterlockingElementLogic`:

- `Validate(context, symbol)`: darf das Element in der Route verwendet werden?
- `Apply(context, symbol, result)`: welche Stellwirkung wird bei erfolgreichem Stellen ausgefuehrt?
- `Release(context, symbol, result)`: was passiert beim Aufloesen?

Basisklasse `DefaultElementInterlockingLogic`:

- `Validate`: sperrt bei `Demo.Blocked = true`.
- `Apply`: verriegelt das Symbol (`result.LockSymbol(symbol.Id)`).
- `Release`: standardmaessig keine Aktion.
- Helper:
  - `ValidateRequiredProperty(...)`: falls Property existiert, muss sie `true/1/yes` sein.
  - `IsPropertyEnabled(...)`: bool-Interpretation fuer Propertywerte.
  - `GetProperty(...)`: rohen Propertywert lesen.

## Klassenuebersicht

| Klasse | Kind | Validate | Apply | Release |
|---|---|---|---|---|
| `TrackInterlockingLogic` | `Track` | Sperrt bei `Demo.Maintenance=true`. | Basisverhalten (Lock). | Basisverhalten. |
| `TrackBlockInterlockingLogic` | `TrackBlock` | Sperrt bei `Demo.ReserveOnly=true` oder Belegung (`context.IsOccupied`). | Basisverhalten (Lock). | Basisverhalten. |
| `LineBlockInterlockingLogic` | `LineBlock` | Sperrt bei `Demo.ReserveOnly`, Belegung, oder wenn eigener/gekoppelter LineBlock `Do67.BlockBlocked=true` ist. | Lock + setzt Richtungs/Block-Properties im Dokument fuer die Route. | Entfernt `Do67.LineBlockDirection` und `Do67.BlockBlocked` am Symbol. |
| `SignalInterlockingLogic` | `Signal` | Kein zusaetzliches Validate (nur Basis). | Nur Startsignal: lockt und setzt gruen, ausser `Demo.HoldRed=true`. | Basisverhalten. |
| `SwitchInterlockingLogic` | `Switch` | Braucht `SwitchCommand`; optional Sperre durch `Demo.LockedPosition` gegen geforderte Lage. | Lock + schreibt Weichenlage ins Dokument + `AddSwitchCommand`. | Basisverhalten. |
| `DoubleSlipSwitchInterlockingLogic` | `DoubleSlipSwitch` | Wie Switch, aber fuer DKW (`Demo.LockedPosition`, benoetigter `SwitchCommand`). | Lock + schreibt DKW-Lage ins Dokument + `AddSwitchCommand`. | Basisverhalten. |
| `LevelCrossingInterlockingLogic` | `LevelCrossing` | Falls `Demo.Closed` gesetzt ist, muss es aktiv sein. | Basisverhalten (Lock). | Basisverhalten. |
| `CrossingInterlockingLogic` | `Crossing` | Sperrt bei `Demo.ConflictingCrossing=true`. | Basisverhalten (Lock). | Basisverhalten. |
| `PlatformInterlockingLogic` | `Platform` | Sperrt bei `Demo.PassengerStopOnly=true`. | Basisverhalten (Lock). | Basisverhalten. |
| `SensorInterlockingLogic` | `Sensor` | Falls `Demo.SensorClear` gesetzt ist, muss es aktiv sein. | Basisverhalten (Lock). | Basisverhalten. |
| `BridgeInterlockingLogic` | `Bridge` | Falls `Demo.BridgeReleased` gesetzt ist, muss es aktiv sein. | Basisverhalten (Lock). | Basisverhalten. |
| `BufferStopInterlockingLogic` | `BufferStop` | Immer Fehler: kann nicht Teil einer Fahrstrasse sein. | Basisverhalten (praktisch nicht erreicht). | Basisverhalten. |
| `TunnelPortalInterlockingLogic` | `TunnelPortal` | Falls `Demo.TunnelClear` gesetzt ist, muss es aktiv sein. | Basisverhalten (Lock). | Basisverhalten. |
| `TurntableInterlockingLogic` | `Turntable` | Falls `Demo.Aligned` gesetzt ist, muss es aktiv sein. | Basisverhalten (Lock). | Basisverhalten. |
| `UncouplerInterlockingLogic` | `Uncoupler` | Falls `Demo.Lowered` gesetzt ist, muss es aktiv sein. | Basisverhalten (Lock). | Basisverhalten. |
| `DepotInterlockingLogic` | `Depot` | Falls `Demo.DepotExitReleased` gesetzt ist, muss es aktiv sein. | Basisverhalten (Lock). | Basisverhalten. |
| `TextLabelInterlockingLogic` | `TextLabel` | Sperrt bei `Demo.OperationalLabel=true`. | Basisverhalten (Lock). | Basisverhalten. |

## Spezielle Utility-Methode

`LineBlockInterlockingLogic.RebuildStateForActiveRoutes(TrackPlanDocument document, IReadOnlyList<RouteResult> activeRoutes)`

- Zweck: baut Richtungs-/Blockzustand aller LineBlocks aus den aktuell aktiven Routen neu auf.
- Ablauf:
  1. Entfernt bestehende `Do67.LineBlockDirection` und `Do67.BlockBlocked`.
  2. Wendet fuer jede aktive Route die LineBlock-Zustandslogik erneut an.
