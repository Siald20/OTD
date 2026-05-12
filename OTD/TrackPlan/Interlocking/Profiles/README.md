# Interlocking Profiles

Diese Datei dokumentiert den Ordner `Interlocking/Profiles`: Profile, Domino-67-Logiken und Property-Helfer.

## Profilklassen

## `DefaultInterlockingProfile`

Datei: `DefaultInterlockingProfile.cs`

Zweck:

- Standard-Zuordnung von `TrackSymbolKind` auf Elementlogik.
- Liefert die Basiskonfiguration fuer das Interlocking.

Methoden:

- `ValidateRoute(context)`: keine globale Zusatzpruefung (immer `null`).
- `ApplyRoute(context, result)`: keine profilweite Zusatzaktion.
- `ReleaseRoute(context, result)`: keine profilweite Zusatzaktion.
- `GetLogic(kind)`: gibt registrierte Logik zurueck, sonst Fallback `TrackInterlockingLogic`.
- `ReplaceLogic(logic)` (protected): ersetzt gezielt die Logik eines `Kind` in abgeleiteten Profilen.

## `Domino67InterlockingProfile`

Datei: `Domino67InterlockingProfile.cs`

Zweck:

- Domino-67-spezifisches Profil.
- Ersetzt selektiv die Default-Logik fuer `Signal`, `TrackBlock`, `LineBlock`, `Switch`, `DoubleSlipSwitch`, `LevelCrossing`.

Methoden:

- `ValidateRoute(context)`:
  - Route muss mindestens 2 Symbole haben.
  - Kein Symbol darf bereits `locked` sein.
  - Keine Kollision mit aktiven Routen (`RoutesConflict`).
  - Startsignal-Flankenschutz (`Do67.FlankProtectionSymbols`) darf nicht belegt sein.
  - Startsignal-Durchrutschweg (`Do67.OverlapSymbols`) darf nicht belegt sein.
- `ApplyRoute(context, result)`:
  - Sucht benachbarte flankenschuetzende Weichen/DKW.
  - Ermittelt sichere Schutzlage je Weiche.
  - Stellt Lage im Dokument, fuegt `SwitchCommand` hinzu, verriegelt die Weiche.

Interne Kernlogik:

- `RoutesConflict(...)`: erkennt Konflikte ueber gemeinsame Symbole/Verbindungen, mit Ausnahme geteilter Grenzsignale in Gegenrichtung.
- `FindFlankProtectionSwitches(...)`: findet geeignete Nachbarweichen ausserhalb der Route.
- `TryFindProtectivePosition(...)`: waehlt eine nicht-gefaehrdende Weichenlage.

## Domino-67-spezifische Elementlogiken

## `Domino67SignalInterlockingLogic`

Datei: `Domino67SignalInterlockingLogic.cs`

- `Validate`: sperrt bei `Do67.TrackLocked=true`.
- `Apply`:
  - Nur Startsignal relevant.
  - Wenn keine offenen AutoClose-BUE in der Route: sofort gruen.
  - Sonst `DelayedAction` mit maximaler BUE-AutoClose-Verzoegerung:
    - schliesst betroffene BUEs (`Do67.Closed=true`),
    - setzt danach Signal gruen, falls `Do67.HoldRed` nicht aktiv.
- `Release`: setzt `Do67.SignalIsGreen=false`, ausser `Do67.SignalKeepGreenOnRelease=true`.

## `Domino67SwitchInterlockingLogic`

Datei: `Domino67SwitchInterlockingLogic.cs`

- `Validate`:
  - braucht gueltigen `SwitchCommand`,
  - sperrt bei `Do67.SwitchLocked`,
  - prueft optionale feste Lage `Do67.RequiredPosition`.
- `Release`:
  - falls `Do67.SwitchReleaseToRequiredPosition=true`, stellt beim Aufloesen auf `Do67.RequiredPosition` zurueck und erzeugt `SwitchCommand`.

## `Domino67DoubleSlipSwitchInterlockingLogic`

Datei: `Domino67DoubleSlipSwitchInterlockingLogic.cs`

- `Validate`: analog zu `Domino67SwitchInterlockingLogic`, fuer DKW.
- `Release`: analoges Rueckstellen auf `Do67.RequiredPosition` bei gesetzter Release-Property.

## `Domino67TrackBlockInterlockingLogic`

Datei: `Domino67TrackBlockInterlockingLogic.cs`

- `Validate`:
  - basiert auf `TrackBlockInterlockingLogic`,
  - sperrt bei `Do67.TrackClosed`,
  - sperrt bei belegtem Durchrutschweg aus `Do67.OverlapSymbols`.
- `Release`: keine zusaetzliche Aktion.

## `Domino67LineBlockInterlockingLogic`

Datei: `Domino67LineBlockInterlockingLogic.cs`

- `Validate`:
  - basiert auf `LineBlockInterlockingLogic`,
  - sperrt bei `Do67.TrackClosed`,
  - sperrt bei belegtem Durchrutschweg aus `Do67.OverlapSymbols`.
- `Release`: verwendet Basisklassen-Release (Direction/Blocked-Reset am Symbol).

## `Domino67LevelCrossingInterlockingLogic`

Datei: `Domino67LevelCrossingInterlockingLogic.cs`

- `Validate`: erlaubt nur, wenn BUE bereits geschlossen (`Do67.Closed`) oder AutoClose aktiv (`Do67.AutoClose`).
- `Apply`: keine direkte Aktion (Schliessen passiert ueber Signal-Logik fuer Reihenfolge "BUE zu, dann Signal gruen").
- `Release`: oeffnet BUE (`Do67.Closed=false`), ausser `Do67.LevelCrossingKeepClosedOnRelease=true`.
- `GetAutoCloseDelay(levelCrossing)`: Delay aus `Do67.AutoCloseDelaySeconds`, sonst Default 10 Sekunden.

## Property-Helfer und Konstanten

## `Domino67PropertyHelper`

Datei: `Domino67PropertyHelper.cs`

- `IsEnabled(TrackSymbol, propertyName)`: bool-Property lesen (`true/1/yes`).
- `IsEnabled(DrawnTrackSymbol, propertyName)`: bool-Property im aktuellen Dokumentzustand lesen.
- `Get(TrackSymbol, propertyName)`: rohen Stringwert lesen.

## `Domino67PropertyNames`

Datei: `Domino67PropertyNames.cs`

- Zentrale, typsichere Konstanten fuer alle verwendeten Do67-Propertynamen.
- Bereiche: Gleis, Block/LineBlock, Weiche, Signal, Bahnuebergang, Sonstige.
