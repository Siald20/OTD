# Interlocking (Stellwerkslogik)

Diese Dokumentation beschreibt die komplette API-Oberflaeche im Ordner `TrackPlan/Interlocking`: zentrale Services, Datenmodelle, Profile, Elementlogiken und Domino-67-Properties.

## Zweck und Ablauf

Der Interlocking-Teil **sucht keine Route**, sondern verarbeitet ein bereits gefundenes `RouteResult` in zwei Phasen:

1. `Validate`: alle Regeln pruefen, ohne Zustand zu aendern.
2. `Apply`: Stellwirkungen sammeln/anwenden (Weichenbefehle, Gruensignale, Verschluesse, Delayed Actions).

Entrypoints:

- `RouteInterlockingService.TrySetRoute(RouteSettingRequest)`
- `RouteInterlockingService.ReleaseRoute(RouteSettingRequest)`
- `StationInterlockingRuntime.TryApplyRoute(...)`

---

## Kern-Schnittstellen

### `IInterlockingElementLogic`

Datei: `IInterlockingElementLogic.cs`

- `TrackSymbolKind Kind { get; }`
- `RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)`
- `void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)`
- `void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)`

Bedeutung:

- `Kind`: fuer welchen `TrackSymbolKind` diese Logik gilt.
- `Validate(...)`: prueft, ob das konkrete Symbol fuer die Route benutzt werden darf.
- `Apply(...)`: schreibt Stellwirkungen in das Ergebnis (z. B. Weichenbefehl, Verriegelung).
- `Release(...)`: rueckgaengig/auflosen beim Aufheben einer aktiven Route.

Vertrag: Logik fuer genau einen Elementtyp (`TrackSymbolKind`).

### `IInterlockingProfile`

Datei: `IInterlockingProfile.cs`

- `string Name { get; }`
- `RouteSettingFailure? ValidateRoute(RouteSettingContext context)`
- `void ApplyRoute(RouteSettingContext context, RouteSettingResultBuilder result)`
- `void ReleaseRoute(RouteSettingContext context, RouteSettingResultBuilder result)`
- `IInterlockingElementLogic GetLogic(TrackSymbolKind kind)`

Bedeutung:

- `Name`: Profilname fuer Meldungen/Anzeige.
- `ValidateRoute(...)`: globale Vorpruefung vor allen Elementpruefungen.
- `ApplyRoute(...)`: profilweite Stellwirkungen nach Element-`Apply`.
- `ReleaseRoute(...)`: profilweite Aufloeselogik.
- `GetLogic(...)`: liefert die zustandige Elementlogik fuer den Elementtyp.

Vertrag: profilweite Regeln plus Zuordnung `Kind -> Elementlogik`.

---

## Zentrale Klassen (oeffentliche API)

### `RouteInterlockingService`

Datei: `RouteInterlockingService.cs`

- `RouteInterlockingService(IInterlockingProfile profile)`
- `RouteSettingResult TrySetRoute(RouteSettingRequest request)`
- `RouteSettingResult ReleaseRoute(RouteSettingRequest request)`

Bedeutung:

- Konstruktor: bindet das aktive Profil.
- `TrySetRoute(...)`: fuehrt globale Profilpruefung, Element-`Validate`, danach Element-`Apply` und Profil-`ApplyRoute` aus; liefert Erfolg/Fehler + Wirkungen.
- `ReleaseRoute(...)`: ruft Element-`Release` und Profil-`ReleaseRoute` auf; liefert Aufloese-Wirkungen.

Hinweis: Fuehrt Delayed Actions asynchron aus und meldet deren Ergebnis ueber `RouteSettingRequest.DelayedActionApplied`.

### `StationInterlockingRuntime`

Datei: `StationInterlockingRuntime.cs`

Zustand:

- `HashSet<string> OccupiedSymbolIds`
- `HashSet<string> ReleaseOnFreeSymbolIds`
- `HashSet<string> GreenSignalIds`
- `HashSet<string> LockedSymbolIds`
- `List<RouteResult> ActiveRoutes`
- `List<RouteResult> StoredRoutes`
- `IReadOnlyList<LineBlockBoundaryLink> LineBlockBoundaryLinks`

Methoden:

- `AddLineBlockBoundaryLink(string localLineBlockId, string remoteLineBlockId)`
- `RemoveLineBlockBoundaryLink(string localLineBlockId, string remoteLineBlockId)`
- `ApplyRemoteLineBlockDirection(string localLineBlockId, string remoteDirection, TrackPlanDocument document)`
- `ApplyRemoteLineBlockState(string localLineBlockId, string remoteDirection, bool isBlocked, TrackPlanDocument document)`
- `IReadOnlyList<LineBlockBoundaryDirectionUpdate> BuildRemoteLineBlockDirectionUpdates(TrackPlanDocument document)`
- `bool TryApplyRoute(RouteResult route, TrackPlanDocument document, bool storeOnFailure, Func<string, bool> canStoreRoute, Action<RouteSettingContext, RouteSettingResultBuilder> delayedActionApplied, out string message, out IReadOnlyList<SwitchCommand> switchCommands)`
- `void ReleaseAllRoutes(TrackPlanDocument document, Action<RouteSettingContext, RouteSettingResultBuilder> delayedActionApplied)`
- `List<RouteResult> ReleaseRoutesContainingSymbol(string symbolId, TrackPlanDocument document, Action<RouteSettingContext, RouteSettingResultBuilder> delayedActionApplied)`
- `int TrySetStoredRoutes(TrackPlanDocument document, Func<string, bool> canStoreRoute, Action<RouteSettingContext, RouteSettingResultBuilder> delayedActionApplied)`
- `void RebuildLockedSymbols()`
- `void ClearAllStates()`

Bedeutung:

- `AddLineBlockBoundaryLink(...)`: verknuepft lokale und entfernte LineBlock-ID (ohne Duplikate).
- `RemoveLineBlockBoundaryLink(...)`: loescht genau diese Kopplung.
- `ApplyRemoteLineBlockDirection(...)`: uebernimmt Richtung vom Nachbarbahnhof gespiegelt (incoming/outgoing invertiert).
- `ApplyRemoteLineBlockState(...)`: setzt Richtung + Blockiertstatus vom Nachbarzustand.
- `BuildRemoteLineBlockDirectionUpdates(...)`: erzeugt aus lokalen Bloecken die zu sendenden Remote-Updates.
- `TryApplyRoute(...)`: versucht zu stellen; bei Erfolg Runtime-Zustaende aktualisieren, bei Fehlschlag optional in `StoredRoutes` merken.
- `ReleaseAllRoutes(...)`: loest alle aktiven Routen auf und leert Verriegelungs-/Release-Zustaende.
- `ReleaseRoutesContainingSymbol(...)`: loest nur Routen mit gegebenem Symbol auf und gibt diese zurueck.
- `TrySetStoredRoutes(...)`: versucht gemerkte Routen erneut zu stellen; Rueckgabe = Anzahl erfolgreicher Stellen.
- `RebuildLockedSymbols()`: baut `LockedSymbolIds` aus `ActiveRoutes` neu auf.
- `ClearAllStates()`: setzt alle Runtime-Mengen/Listen zurueck.

---

## Request/Context/Result-Modelle

### `RouteSettingRequest` (`RouteSettingRequest.cs`)

- `RouteResult Route` (required)
- `TrackPlanDocument Document` (required)
- `IReadOnlySet<string> OccupiedSymbolIds`
- `IReadOnlyList<RouteResult> ActiveRoutes`
- `IReadOnlySet<string> LockedSymbolIds`
- `Action<RouteSettingContext, RouteSettingResultBuilder>? DelayedActionApplied`

Bedeutung:

- `Route`: zu stellende, bereits gefundene Route.
- `Document`: aktueller veraenderbarer Gleisplan.
- `OccupiedSymbolIds`: externe Belegungslage.
- `ActiveRoutes`: aktuell gestellte Routen (fuer Konfliktpruefung).
- `LockedSymbolIds`: bereits verriegelte Symbole.
- `DelayedActionApplied`: Callback fuer spaeter ausgefuehrte Delayed Actions.

### `RouteSettingContext` (`RouteSettingContext.cs`)

- `RouteSettingRequest Request`
- `bool IsOccupied(string symbolId)`
- `bool IsLocked(string symbolId)`
- `DrawnTrackSymbol? FindDrawnSymbol(string symbolId)`
- `bool RouteContains(string symbolId)`
- `IReadOnlyList<DrawnTrackConnection> GetDrawnConnections(string symbolId)`
- `string? GetPortOnSymbol(DrawnTrackConnection connection, string symbolId)`
- `string? GetOtherSymbolId(DrawnTrackConnection connection, string symbolId)`
- `bool AnySymbolOccupied(IEnumerable<string> symbolIds)`
- `IReadOnlyList<string> ParseSymbolList(TrackSymbol symbol, string propertyName)`
- `bool TryGetSwitchCommand(string switchId, out SwitchCommand command)`
- `void SetSymbolProperty(string symbolId, string propertyName, string value)`
- `void SetSymbolProperty(string symbolId, string propertyName, bool value)`

Bedeutung:

- `Request`: Zugriff auf Original-Request.
- `IsOccupied(...)`: prueft Belegung aus `OccupiedSymbolIds`.
- `IsLocked(...)`: prueft Verriegelung aus `LockedSymbolIds`.
- `FindDrawnSymbol(...)`: findet Symbol im Dokument.
- `RouteContains(...)`: prueft, ob Symbol Teil der aktuellen Route ist.
- `GetDrawnConnections(...)`: gibt alle Verbindungen eines Symbols im Dokument.
- `GetPortOnSymbol(...)`: liefert den Port-Namen einer Verbindung bezogen auf ein Symbol.
- `GetOtherSymbolId(...)`: liefert Gegenstelle einer Verbindung.
- `AnySymbolOccupied(...)`: true, wenn mindestens ein Symbol belegt ist.
- `ParseSymbolList(...)`: liest CSV-Property (`a,b,c`) als Liste.
- `TryGetSwitchCommand(...)`: liefert berechneten Weichenbefehl der Route.
- `SetSymbolProperty(..., string)`: setzt/ueberschreibt eine Symbol-Property im Dokument.
- `SetSymbolProperty(..., bool)`: bool-Variante (`true`/`false` als String).

### `RouteSettingResult` (`RouteSettingResult.cs`)

- `bool IsSuccess`
- `string Message`
- `IReadOnlyList<SwitchCommand> SwitchCommands`
- `IReadOnlySet<string> GreenSignalIds`
- `IReadOnlySet<string> LockedSymbolIds`
- `IReadOnlyList<DelayedAction> DelayedActions`
- `static RouteSettingResult Failed(string message)`
- `static RouteSettingResult Success(RouteSettingResultBuilder builder, string message)`

Bedeutung:

- `IsSuccess`: Status des Stell-/Aufloeseversuchs.
- `Message`: Fachmeldung fuer UI/Log.
- `SwitchCommands`: zu stellende Weichenbefehle.
- `GreenSignalIds`: Signale, die Fahrt zeigen duerfen.
- `LockedSymbolIds`: durch Ergebnis verriegelte Symbole.
- `DelayedActions`: verzogert auszufuehrende Nachwirkungen.
- `Failed(...)`: erstellt Fehlerergebnis ohne Wirkungen.
- `Success(...)`: materialisiert Ergebnis aus dem Builder.

### `RouteSettingResultBuilder` (`RouteSettingResultBuilder.cs`)

- `IReadOnlyList<SwitchCommand> SwitchCommands`
- `IReadOnlySet<string> GreenSignalIds`
- `IReadOnlySet<string> LockedSymbolIds`
- `IReadOnlyList<DelayedAction> DelayedActions`
- `void AddSwitchCommand(SwitchCommand command)`
- `void AddGreenSignal(string signalId)`
- `void LockSymbol(string symbolId)`
- `void AddDelayedAction(DelayedAction action)`

Bedeutung:

- Properties: read-only Sicht auf gesammelte Wirkungen.
- `AddSwitchCommand(...)`: fuegt Weichenbefehl hinzu.
- `AddGreenSignal(...)`: markiert Signal als gruen.
- `LockSymbol(...)`: markiert Symbol als verriegelt.
- `AddDelayedAction(...)`: plant verzogerte Folgeaktion.

### Weitere Typen

- `RouteSettingFailure` (`Message`)
- `DelayedAction(TimeSpan delay, Action<RouteSettingContext, RouteSettingResultBuilder> action)`
- `LineBlockBoundaryLink(string LocalLineBlockId, string RemoteLineBlockId)`
- `LineBlockBoundaryDirectionUpdate(string RemoteLineBlockId, string RemoteDirection, bool IsBlocked)`

---

## Profile

### `DefaultInterlockingProfile` (`Profiles/DefaultInterlockingProfile.cs`)

- Name: `"Default"`
- Standard-Mapping von `TrackSymbolKind` auf Default-Elementlogiken
- Erweiterungspunkt: `protected void ReplaceLogic(IInterlockingElementLogic logic)`

### `Domino67InterlockingProfile` (`Profiles/Domino67InterlockingProfile.cs`)

- Name: `"Domino 67"`
- Ersetzt Logik fuer:
  - `Signal`
  - `TrackBlock`
  - `LineBlock`
  - `Switch`
  - `DoubleSlipSwitch`
  - `LevelCrossing`
- Enthaelt profilweite Zusatzpruefungen (Konflikte, Flankenschutz, Durchrutschweg) und Flankenschutz-Anwendung.

---

## Elementlogiken

Basisklasse:

- `DefaultElementInterlockingLogic` (`Elements/DefaultElementInterlockingLogic.cs`)
  - implementiert `IInterlockingElementLogic`
  - Default-Verhalten: Demo-Property pruefen (`Demo.Blocked`) und Symbol beim Stellen verriegeln

Verfuegbare konkrete Logiken:

- `BridgeInterlockingLogic`
- `BufferStopInterlockingLogic`
- `CrossingInterlockingLogic`
- `DepotInterlockingLogic`
- `DoubleSlipSwitchInterlockingLogic`
- `LevelCrossingInterlockingLogic`
- `LineBlockInterlockingLogic`
- `PlatformInterlockingLogic`
- `SensorInterlockingLogic`
- `SignalInterlockingLogic`
- `SwitchInterlockingLogic`
- `TextLabelInterlockingLogic`
- `TrackBlockInterlockingLogic`
- `TrackInterlockingLogic`
- `TunnelPortalInterlockingLogic`
- `TurntableInterlockingLogic`
- `UncouplerInterlockingLogic`

Domino-67-spezifische Varianten:

- `Domino67SignalInterlockingLogic`
- `Domino67TrackBlockInterlockingLogic`
- `Domino67LineBlockInterlockingLogic`
- `Domino67SwitchInterlockingLogic`
- `Domino67DoubleSlipSwitchInterlockingLogic`
- `Domino67LevelCrossingInterlockingLogic`

---

## Domino-67 Property API

Datei: `Profiles/Domino67PropertyNames.cs`

### Gleis

- `Do67.TrackOccupied`
- `Do67.TrackClosed`
- `Do67.TrackLocked`
- `Do67.TrackError`

### Block / LineBlock

- `Do67.BlockBlocked`
- `Do67.BlockClosed`
- `Do67.BlockError`
- `Do67.LineBlockDirection`
- Richtungswerte: `incoming`, `outgoing`

### Weiche

- `Do67.SwitchLocked`
- `Do67.FlankProtectionSymbols`
- `Do67.FlankProtectionEnabled`
- `Do67.RequiredPosition`
- `Do67.SwitchError`
- `Do67.SwitchOccupied`

### Signal

- `Do67.HoldRed`
- `Do67.SignalIsGreen`
- `Do67.SignalKeepGreenOnRelease`
- `Do67.SignalIsRed`
- `Do67.SignalError`

### Bahnuebergang

- `Do67.Closed`
- `Do67.AutoClose`
- `Do67.AutoCloseDelaySeconds`
- `Do67.LevelCrossingKeepClosedOnRelease`
- `Do67.LevelCrossingOpen`
- `Do67.LevelCrossingError`
- `Do67.LevelCrossingIsClosing`
- `Do67.LevelCrossingIsOpening`
- `Do67.LevelCrossingOccupied`

### Sonstige

- `Do67.OverlapSymbols`
- `Do67.ReleaseTrigger`
- `Do67.SwitchReleaseToRequiredPosition`

---

## Erweitern (kurz)

1. Neue Elementlogik als Klasse mit `IInterlockingElementLogic` (oder Ableitung von `DefaultElementInterlockingLogic`) erstellen.
2. Im Profil registrieren (Default direkt im Dictionary, bei Spezialprofilen via `ReplaceLogic(...)`).
3. Optional neue Property-Namen zentral in `Domino67PropertyNames` aufnehmen.

---

## Quick Reference (Methode | Input | Output | Effekt)

| Methode | Input | Output | Effekt |
|---|---|---|---|
| `RouteInterlockingService.TrySetRoute` | `RouteSettingRequest` | `RouteSettingResult` | Prueft Route und sammelt/erzeugt Stellwirkungen fuer das Stellen. |
| `RouteInterlockingService.ReleaseRoute` | `RouteSettingRequest` | `RouteSettingResult` | Fuehrt Aufloeselogik fuer eine aktive Route aus. |
| `StationInterlockingRuntime.TryApplyRoute` | `RouteResult`, `TrackPlanDocument`, Flags/Callbacks | `bool` + `out message`, `out switchCommands` | Versucht Route zu stellen und aktualisiert Runtime-Zustaende bei Erfolg. |
| `StationInterlockingRuntime.ReleaseAllRoutes` | `TrackPlanDocument`, Callback | `void` | Loest alle aktiven Routen auf und bereinigt Runtime-Zustaende. |
| `StationInterlockingRuntime.ReleaseRoutesContainingSymbol` | `symbolId`, `TrackPlanDocument`, Callback | `List<RouteResult>` | Loest alle Routen auf, die das Symbol enthalten. |
| `StationInterlockingRuntime.TrySetStoredRoutes` | `TrackPlanDocument`, Predicate, Callback | `int` | Versucht gespeicherte Routen erneut zu stellen; gibt Erfolgsanzahl zurueck. |
| `StationInterlockingRuntime.RebuildLockedSymbols` | - | `void` | Baut `LockedSymbolIds` aus `ActiveRoutes` neu auf. |
| `StationInterlockingRuntime.ClearAllStates` | - | `void` | Leert alle Runtime-Collections (Belegung, Verriegelung, aktive/gespeicherte Routen). |
| `StationInterlockingRuntime.AddLineBlockBoundaryLink` | lokale/remote LineBlock-ID | `void` | Fuegt Boundary-Link hinzu (ohne Duplikate). |
| `StationInterlockingRuntime.RemoveLineBlockBoundaryLink` | lokale/remote LineBlock-ID | `void` | Entfernt Boundary-Link. |
| `StationInterlockingRuntime.ApplyRemoteLineBlockDirection` | lokale ID, Remote-Richtung, Dokument | `void` | Uebernimmt Remote-Richtung gespiegelt in lokale Property. |
| `StationInterlockingRuntime.ApplyRemoteLineBlockState` | lokale ID, Richtung, blocked, Dokument | `void` | Setzt lokale Richtung und Blockzustand gemappt vom Remote-Zustand. |
| `StationInterlockingRuntime.BuildRemoteLineBlockDirectionUpdates` | `TrackPlanDocument` | `IReadOnlyList<LineBlockBoundaryDirectionUpdate>` | Erzeugt aus lokalen Bloecken die zu sendenden Remote-Updates. |

| Methode | Input | Output | Effekt |
|---|---|---|---|
| `IInterlockingProfile.ValidateRoute` | `RouteSettingContext` | `RouteSettingFailure?` | Globale Profilpruefung vor Elementpruefungen. |
| `IInterlockingProfile.ApplyRoute` | `RouteSettingContext`, `RouteSettingResultBuilder` | `void` | Profilweite Stellwirkungen nach Element-Apply. |
| `IInterlockingProfile.ReleaseRoute` | `RouteSettingContext`, `RouteSettingResultBuilder` | `void` | Profilweite Aufloesewirkungen. |
| `IInterlockingProfile.GetLogic` | `TrackSymbolKind` | `IInterlockingElementLogic` | Liefert Elementlogik fuer den Typ. |
| `IInterlockingElementLogic.Validate` | `RouteSettingContext`, `TrackSymbol` | `RouteSettingFailure?` | Prueft ein einzelnes Element auf Stellbarkeit. |
| `IInterlockingElementLogic.Apply` | `RouteSettingContext`, `TrackSymbol`, `RouteSettingResultBuilder` | `void` | Schreibt Element-Stellwirkungen ins Ergebnis. |
| `IInterlockingElementLogic.Release` | `RouteSettingContext`, `TrackSymbol`, `RouteSettingResultBuilder` | `void` | Schreibt Element-Aufloesewirkungen ins Ergebnis. |

| Methode | Input | Output | Effekt |
|---|---|---|---|
| `RouteSettingContext.IsOccupied` | `symbolId` | `bool` | Prueft Belegung aus Request-Zustand. |
| `RouteSettingContext.IsLocked` | `symbolId` | `bool` | Prueft Verriegelung aus Request-Zustand. |
| `RouteSettingContext.FindDrawnSymbol` | `symbolId` | `DrawnTrackSymbol?` | Findet Symbol im Dokument. |
| `RouteSettingContext.RouteContains` | `symbolId` | `bool` | Prueft, ob Symbol in aktueller Route liegt. |
| `RouteSettingContext.GetDrawnConnections` | `symbolId` | `IReadOnlyList<DrawnTrackConnection>` | Liefert alle Verbindungen eines Symbols. |
| `RouteSettingContext.GetPortOnSymbol` | `connection`, `symbolId` | `string?` | Liefert Portname der Verbindung auf diesem Symbol. |
| `RouteSettingContext.GetOtherSymbolId` | `connection`, `symbolId` | `string?` | Liefert Gegen-Symbol-ID einer Verbindung. |
| `RouteSettingContext.AnySymbolOccupied` | `IEnumerable<string>` | `bool` | True, wenn eines der Symbole belegt ist. |
| `RouteSettingContext.ParseSymbolList` | `TrackSymbol`, `propertyName` | `IReadOnlyList<string>` | Liest CSV-Property als ID-Liste. |
| `RouteSettingContext.TryGetSwitchCommand` | `switchId` | `bool` + `out SwitchCommand` | Liefert den zur Route berechneten Weichenbefehl. |
| `RouteSettingContext.SetSymbolProperty` | `symbolId`, `propertyName`, `string/bool` | `void` | Schreibt Property-Wert ins Dokument-Symbol. |

| Methode | Input | Output | Effekt |
|---|---|---|---|
| `RouteSettingResult.Failed` | `message` | `RouteSettingResult` | Fehlerergebnis ohne Stellwirkungen. |
| `RouteSettingResult.Success` | `RouteSettingResultBuilder`, `message` | `RouteSettingResult` | Erfolgsergebnis aus gesammelten Wirkungen. |
| `RouteSettingResultBuilder.AddSwitchCommand` | `SwitchCommand` | `void` | Fuegt Weichenbefehl hinzu. |
| `RouteSettingResultBuilder.AddGreenSignal` | `signalId` | `void` | Markiert Signal als gruen. |
| `RouteSettingResultBuilder.LockSymbol` | `symbolId` | `void` | Markiert Symbol als verriegelt. |
| `RouteSettingResultBuilder.AddDelayedAction` | `DelayedAction` | `void` | Plant verzoegerte Folgeaktion. |
