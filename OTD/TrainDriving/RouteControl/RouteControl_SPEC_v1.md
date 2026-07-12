# RouteControl SPEC v1.2

Status: Draft v1.2  
Kontext: `OTD/TrainDriving/RouteControl/`  
Architekturmodus: Greenfield (keine Rückwärtskompatibilität erforderlich)

## 1. Ziel

Das RouteControl verwaltet den vor dem Zug liegenden Fahrweg (Route) als geordnete Folge von `RouteLeg`-Elementen und übernimmt die **vollständige Laufsteuerung**:

- validiert und verwaltet die RouteTable,
- erzeugt direkt Fahrkommandos aus den RouteLeg-Einträgen,
- passt Fahrverhalten bei Updates sofort an,
- koppelt sich an Rueckmeldungen/Events fuer Korrektur und Sicherheit.

Zur Ausführung von Zugbewegungen wird `Trajectory` verwendet.
`Trajectory` ist dabei kein eigenständiger Planer, sondern ein untergeordnetes Rechen-/Ausführungsmodul des RouteControl.

## 2. Begriffe

- **Route (Fahrweg)**: Der gesamte Ablauf — die vollständige geordnete Folge aller `RouteLeg`-Elemente in der `RouteTable` bis zum Ende der bekannten Strecke.
- **RouteLeg (Streckenabschnitt)**: Einzelnes Teilstück des Fahrwegs von `FromWaypointId` nach `ToWaypointId`; elementarer Eintrag in der `RouteTable`.
- **Waypoint**: Eindeutig identifizierter Fixpunkt innerhalb des topologischen Layouts (`railwaylayout.xml`), entweder auf einem Knoten oder innerhalb einer Kante; bildet gleichzeitig den Anfang und das Ende eines automatisch generierten `RouteLeg`.
- **RouteTable**: Geordnete Liste der `RouteLeg`-Elemente der aktiven Route; sie umfasst kommende `RouteLeg`-Elemente, den aktiven `RouteLeg` sowie zurückliegende `RouteLeg`-Elemente, solange sich der Zugschluss noch darin befindet.
- **Aktiver RouteLeg**: Der `RouteLeg`, in dem sich die Zugspitze aktuell befindet.
- **Konsumierter RouteLeg**: Vollständig abgefahrener `RouteLeg`. Er gilt erst dann als konsumiert, wenn der Zugschluss den Streckenabschnitt verlassen hat; danach wird er aus der `RouteTable` entfernt.
- **StopPoint**: Positionsgebundener Haltepunkt innerhalb eines `RouteLeg` (Offset relativ zu `FromWaypointId`), an dem der Zug planmäßig auf `0` abbremsen soll. Verwendungszweck: Haltepunkt auf offener Strecke, Bahnsteighalt vor Abschnittsende und allgemein positionsgebundenes Anhalten an Bahnsteigen.
- **Vorlauf-Bremsung**: Bremsvorgang, der zur Erreichung eines `StopPoint` bereits im vorhergehenden `RouteLeg` beginnt, damit der Zug bis zum `StopPoint` im Folgeabschnitt natürliches Bremsverhalten zeigt.
- **Impliziter End-Halt**: Automatisches Bremsziel am Ende der bekannten Strecke nach dem letzten `RouteLeg`; kein eigener Datensatz, sondern vom RouteControl fahrdynamisch als Halt auf `0` eingeplant.
## 3. Rollen und Verantwortlichkeiten

### 3.1 Stellwerksinstanz (Interlocking, externe Quelle)

- verwaltet den Soll-Fahrweg fachlich, basierend auf einzelnen Fahrstraßen,
- liefert Add-Operationen (einzeln oder als Liste) am Tabellenende,
- liefert Replace-Operationen über `ReplaceRoutes(...)` (Startposition aus `legs[0].FromWaypointId`) sowie gezielt per `ReplaceRouteAtEnd(...)`,
- liefert Remove-Operationen (letzter Eintrag oder ab Waypoint),
- erhält Rückmeldungen (Warnungen, Ereignisse, Sicherheitsstopp).

Hinweis: Das Stellwerk kennt ausschließlich Wegpunkte. `ReplaceRoutes` ist daher der primäre Update-Einstiegspunkt für das Stellwerk.

### 3.2 RouteControl (zentral)

- prüft Plausibilität und Kontextkonsistenz,
- verwaltet aktive Tabelle inkl. Konsumierung,
- steuert Fahrdynamik/Fahrbefehle direkt,
- ueberwacht Rueckmelde-Timings und loest Sicherheitsreaktionen aus.

## 4. Domänenmodell (statische Daten)

### 4.1 RouteLeg (Pflichtfelder)

- `FromWaypointId: string` (nicht leer, eindeutig in der RouteTable)
- `ToWaypointId: string` (nicht leer, ungleich `FromWaypointId`)
- `DistanceCm: int` (`> 0`, Pflicht; wird aus der Layout-Topologie zwischen zwei Waypoints berechnet)
- `MaxSpeedKmh: double` (`> 0`, Pflicht; beschreibt die zulässige Höchstgeschwindigkeit auf diesem Abschnitt)
- `DriveProfile: RouteDriveProfile?`
- `AccelerationStartPolicy: AccelerationStartPolicy` (Default `AtWaypointCrossing`)
- `Metadata: RouteMetadata?` (optional; kann u. a. Signalbegriffe am Anfang und/oder Ende der Route enthalten)
- `FeedbackActivationPoints: IReadOnlyList<FeedbackActivationPoint>?` (optional; positionsgebundene Rueckmeldungen relativ zum Beginn des `RouteLeg`, automatisch aus dem Layout abgeleitet)

```csharp
public enum AccelerationStartPolicy
{
    AtWaypointCrossing = 0,
    AfterTrainClearsWaypoint = 1
}
```

Hinweis:
- `DistanceCm == 0` und `MaxSpeedKmh == 0` sind in einem `RouteLeg` **nicht zulässig**. Jeder Tabelleneintrag beschreibt einen befahrbaren Abschnitt mit positiver Ausdehnung.
- Der Halt am Ende der bekannten Strecke ist **implizit**: Das RouteControl bremst den Zug nach dem letzten `RouteLeg` automatisch auf 0.
- Dieser implizite End-Halt wird fahrdynamisch **bereits innerhalb des letzten `RouteLeg` als reguläres Bremsziel geplant**; der Zug darf also nicht erst am Tabellenende abrupt von der aktuellen Fahrgeschwindigkeit auf 0 wechseln.
- Signalaspekte und Fahrbegriffe werden bei Bedarf als Metadaten in `RouteMetadata` am Anfang (`EntrySignal`) und/oder Ende (`ExitSignal`) der Route geführt.

### 4.2 Marker und Events

- `FeedbackActivationPoint`: positionsgebundener Realwelt-Abgleich innerhalb eines `RouteLeg`
  - `FeedbackId`, `OffsetCm`, optional `ActivationTimeoutMs`
  - Entsteht beim Laden aus Rueckmeldern des Layouts entlang des topologischen Pfads zwischen `FromWaypointId` und `ToWaypointId`.
  - `occupancy` (`FeedbackType.OccupancyFeedback`) liegt an der Einfahrtsseite des Host-Tracks, `contact` (`FeedbackType.ContactFeedback`) an einem festen geometrischen Offset.
- `FeedbackReference`: Altmodell / reserviert; wird im aktuellen Greenfield-Modell nicht mehr zur Leg-Erzeugung verwendet.
- `RouteActionEvent`: positionsgebundene Aktion (z.B. Pfeifen vor unbewachtem Bahnübergang)
- `RoutePositionEvent`: positionsgebundene Rückmeldung ans Stellwerk (z.B. virtuelle Blockgrenzen)

### 4.3 StopPoint und Halteverhalten

- `StopPoint`: positionsgebundener Haltepunkt innerhalb eines `RouteLeg`.
  - `OffsetCm`: Abstand vom Beginn des `RouteLeg` in cm
  - optional `StopReason`
  - optional `Metadata`
- `ReleaseGo`: explizite Weiterfahrfreigabe nach einem erreichten `StopPoint`.

Regeln:
- `0 <= OffsetCm <= DistanceCm` des referenzierten `RouteLeg`.
- Pro `RouteLeg` ist 0..1 `StopPoint` zulässig.
- Ein `StopPoint` kann sehr früh innerhalb eines `RouteLeg` liegen.
- Sobald ein `StopPoint` definiert ist, erfolgt an dieser Position immer ein Halt (`v = 0`).
- Die Weiterfahrt nach einem `StopPoint` erfolgt ausschließlich über `ReleaseGo()`.
- Die Länge des Bremswegs wird aus dem `RouteLeg` mit dem `StopPoint` abgeleitet; die Bremsstrecke bleibt damit grundsätzlich unverändert.
- Liegt der `StopPoint` vor dem rechnerischen Bremsbeginn, wird der Brems-Beginn um die Distanz zwischen `StopPoint` und Ende des `RouteLeg` in den vorausgehenden `RouteLeg` zurückverschoben.
- Der Bremsvorgang darf also in einem vorherigen `RouteLeg` beginnen, auch wenn das Ziel des Bremsens ein `StopPoint` im Folge-`RouteLeg` ist.

## 5. Laufzeitzustand (dynamisch)

```csharp
public sealed record RouteRuntimeState(
    string? ActiveFromWaypointId,
    int? ActiveRouteIndex,
    double HeadPositionCm,
    double TrainLengthCm,
    bool ActiveStopPoint,
    bool SafetyStopInjected,
    bool FeedbackInputRecoveryMode,
    int ConsumedRouteCount);
```

Bedeutung:
- `ActiveStopPoint == true`: Der `StopPoint` des aktiven `RouteLeg` wurde erreicht; der Zug steht auf `0` und wartet auf `ReleaseGo()`.
- `ActiveStopPoint == false`: Kein aktiver StopPoint-Wartezustand; es ist keine Go-Freigabe aus dem StopPoint-Mechanismus erforderlich.
- `SafetyStopInjected`: Sicherheitsrückfall; das RouteControl begrenzt Fahrbefehle so, dass am Ende der bekannten Strecke sicher `0` erreicht wird.
- `FeedbackInputRecoveryMode`: Nach Nothalt/Abweichung Weiterfahrt nur stark reduziert bis zum naechsten Rueckmelder.

## 6. Invarianten

1. Kettenkonsistenz: `RouteLeg[i].ToWaypointId == RouteLeg[i+1].FromWaypointId`.
2. Keine doppelten `FromWaypointId` in der RouteTable (Eindeutigkeit als Lookup-Schlüssel).
3. `DistanceCm > 0`, `MaxSpeedKmh > 0` für alle `RouteLeg`-Instanzen in der RouteTable (jeder Abschnitt hat positive Ausdehnung und positive zulässige Höchstgeschwindigkeit).
4. Marker-/Event-Daten müssen gültig sein.
5. Aktiver RouteLeg ist für Add/Replace/Remove gesperrt.
6. Konsumierte RouteLeg-Elemente sind nicht mehr adressierbar und für keine Operation zugelassen.
7. Add/Replace/Remove-Operationen sind atomar (Alles-oder-Nichts).
8. Der implizite End-Halt ist kein `RouteLeg` und kann nicht Ziel einer Operation sein.
9. Jeder `StopPoint` referenziert einen vorhandenen `RouteLeg`, und sein `OffsetCm` liegt innerhalb der Distanz dieses `RouteLeg`.
10. Der Bremsbeginn zu einem `StopPoint` darf bei frühem StopPoint in den vorhergehenden `RouteLeg` zurückverlagert werden, die Bremsweg-Länge bleibt dabei durch den Zielabschnitt definiert.
11. Pro `RouteLeg` ist höchstens ein `StopPoint` zulässig.

## 7. API-Vertrag (RouteControl)

### 7.1 Datenpflege durch Stellwerk

```csharp
void AddRoute(RouteLeg leg);
void AddRoutes(IReadOnlyList<RouteLeg> legs);
void ReplaceRoutes(IReadOnlyList<RouteLeg> legs);
void ReplaceRouteAtEnd(RouteLeg leg);
void RemoveRouteAtEnd();
void RemoveRoutesFromWaypoint(string fromWaypointId);
```

Regeln:
- Einfügen in eine bestehende RouteLeg-Folge ist nicht vorgesehen (keine Insert-Operationen), da die Gleisabschnittsfolge topologisch fest ist.
- `AddRoute`/`AddRoutes` sind **append-only**: fügen ausschließlich am Tabellenende an, ersetzen nichts und überschreiben keinen End-Halt, da dieser kein Datensatz ist.
- `ReplaceRoutes` ersetzt atomar ab der durch `legs[0].FromWaypointId` identifizierten Position bis Tabellenende.
- `ReplaceRouteAtEnd` ersetzt atomar genau den letzten `RouteLeg` der RouteTable.
- `RemoveRouteAtEnd` entfernt atomar genau den letzten `RouteLeg` der RouteTable.
- `RemoveRoutesFromWaypoint` entfernt atomar alle `RouteLeg`-Elemente ab der durch `fromWaypointId` identifizierten Position bis Tabellenende.
- Für **alle** Replace/Remove-Operationen gilt: Der Zielbereich darf **keinen aktiven** und **keinen konsumierten** `RouteLeg` enthalten.
- Für `ReplaceRoutes` muss `legs` mindestens einen Eintrag enthalten; `legs[0].FromWaypointId` dient als Startposition der Ersetzung.
- `ReplaceRouteAtEnd` schlägt mit `RouteValidationException` fehl, wenn die RouteTable leer ist.
- `ReplaceRoutes` und `RemoveRoutesFromWaypoint` schlagen mit `RouteValidationException` fehl, wenn die Startposition nicht eindeutig in der RouteTable vorhanden ist.
- Kontextprüfung auf Kette/Wegpunkte ist für Add/Replace verpflichtend.

### 7.2 Erzeugung und Laufzeitsteuerung

```csharp
RouteController(Train train, bool initialHold = false);
void AdvancePosition(double headPositionCm);
void OnFeedbackInputActivated(int feedbackId);
void ReleaseGo();
void OnEmergencyStop();
void OnEmergencyRelease();
RouteSnapshot GetSnapshot();
```

Regeln:
- Die Bindung an `Train` erfolgt über den `RouteController`-Konstruktor.
- Über `BoundTrain` sind grundlegende Zugfunktionen (z. B. Betriebsmodus, Fahrtrichtung) verfügbar.
- RouteControl generiert Fahrbefehle direkt aus den RouteLeg-Elementen.
- Bei Route-Update wird Fahrstrategie sofort neu berechnet.
- `OnFeedbackInputActivated(feedbackId)` ermoeglicht positionsgebundene Rekalibrierung anhand der in `RouteLeg.FeedbackActivationPoints` konfigurierten Rueckmelder.
- Nach Erreichen eines `StopPoint` ist Weiterfahrt nur über `ReleaseGo()` zulässig.
- `ReleaseGo()` gibt die Weiterfahrt frei — verhält sich abhängig vom aktiven Haltezustand:
  - Ist ein **InitialHold** aktiv, wird dieser zuerst konsumiert (einmalig); danach wirkt `ReleaseGo()` ausschliesslich auf `StopPoint`-Halte.
  - Ist kein InitialHold aktiv, gibt `ReleaseGo()` den aktuell anstehenden `StopPoint`-Halt frei.
- `OnEmergencyRelease()` kann `FeedbackInputRecoveryMode` aktivieren (Langsamfahrt bis zum naechsten Rueckmelder).

#### InitialHold (Startzustand)

```csharp
new RouteController(train, initialHold: true)
```

- **Zweck**: Stellt sicher, dass der Zug nach dem erstmaligen Eintragen von RouteLeg-Elementen in die leere RouteTable **nicht sofort losfährt**, sondern auf einen expliziten `ReleaseGo()`-Aufruf wartet.
- **Anwendungsfall**: Stellwerk bereitet Fahrweg vor (Weichen stellen, Route aufbauen) und gibt erst danach die Abfahrt frei.
- **Verhalten**:
  - Solange `InitialHold` aktiv ist, hält das RouteControl den Zug auf `v = 0`, auch wenn RouteLegs vorhanden sind.
  - `ReleaseGo()` konsumiert den `InitialHold` einmalig; alle weiteren `ReleaseGo()`-Aufrufe wirken danach ausschliesslich auf `StopPoint`-Halte.
  - Der `InitialHold` ist orthogonal zum `StopPoint`-Mechanismus: Er betrifft ausschliesslich den Systemzustand beim ersten Start, nicht positionsgebundene Halte innerhalb eines RouteLeg.
- **Default**: `initialHold: false` — bisheriges Verhalten ohne Wartezeit beim ersten Route-Eintrag.

### 7.3 Live-Updates auf aktiven RouteLeg (z.B. für ETCS L3 Implementierung)

```csharp
void UpdateActiveRouteLeg(string fromWaypointId, int? newDistanceCm = null, double? newMaxSpeedKmh = null);
```

Regeln:
- **Anwendungskontext**: Unterstützt dynamische Bewegungserlaubnisse nach ETCS Level 3 (keine festen Streckenblöcke, sondern „Movement Authorities" die sich live ändern können).
- **Gültig nur für aktiven `RouteLeg`**: Der Zug muss sich aktuell in diesem Abschnitt befinden (`ActiveRouteIndex` und `FromWaypointId` entsprechen).
- **Erlaubte Änderungen**:
  - `DistanceCm` erhöhen (Verlängerung des Fahrwegs): immer erlaubt.
  - `MaxSpeedKmh` erhöhen (höhere Geschwindigkeit zulassen): immer erlaubt.
- **Nicht erlaubte Änderungen**:
  - `DistanceCm` senken: schlägt fehl mit `RouteUpdateConflictException` (Zug könnte bereits über das neue Ziel hinaus sein).
  - `MaxSpeedKmh` senken: schlägt fehl mit `RouteUpdateConflictException` (würde sofortiges Bremsmanöver erzwingen; nutze stattdessen normalen Route-Replace für kontrolliertes Abbremsen).
- **Validierung**:
  - Neue Werte müssen `> 0` sein.
  - Wenn ein `StopPoint` im aktiven `RouteLeg` definiert ist, muss sein `OffsetCm` noch `<= newDistanceCm` sein.
  - Update ist atomar: Alles-oder-Nichts.
- **Fahrstrategie-Neuberechnung**: Bei erfolgreichem Update wird die Fahrstrategie sofort neu berechnet (z. B. beschleunigte Fahrt bei erhöhter `MaxSpeedKmh`).

## 7.4 Events (nach außen)

```csharp
public enum RouteControlEvent
{
    SafetyWarningRaised,
    SafetyStopInjected,
    FeedbackTimeoutRaised
}
```

## 8. Fahrlogik

### 8.1 Geschwindigkeitsübergänge

#### 8.1.1 Bremsen bei Geschwindigkeitsreduktion

- Folge-RouteLeg langsamer: RouteControl bremst den Zug bis zum Beginn des Folge-RouteLeg ab, damit die niedrigere Höchstgeschwindigkeit eingehalten wird.

#### 8.1.2 Beschleunigung bei Geschwindigkeitserhöhung

Wenn ein Folge-RouteLeg eine höhere Geschwindigkeit als der aktuelle RouteLeg zulässt, wird die Beschleunigung nach dem `AccelerationStartPolicy` eingeleitet:

**`AtWaypointCrossing` (Standard)**
- Beschleunigung beginnt **unmittelbar** bei Passieren des Waypoints durch die Zugspitze.
- Anwendung: Offene Strecke, keine Sicherheitsbedenken nach Wegpunktpassage (z.B. Ausfahrt aus Bahnhof auf freier Strecke).

**`AfterTrainClearsWaypoint`**
- Beschleunigung wird verzögert: Sie beginnt erst dann, wenn der **gesamte Zug** (einschließlich Zugschluss) das Waypoint vollständig verlassen hat und den neuen `RouteLeg` vollständig belegt.
- Anwendung: Sicherheitsroutes nach Langsamfahrtabschnitten über Weichen oder durch Kurven, bei denen die Zugkonfiguration bis zur vollständigen Neuausrichtung nicht sofort erhöht beschleunigt werden soll.
- Beispiele: Übergang nach eng befahrener Weichenpartie, Ausstieg aus Langsamfahrtkurve, Transition nach Bahnsteighalt mit variablem Längsprofil.

#### 8.1.3 Halt am Tabellenende

- RouteControl leitet am Ende der bekannten Strecke (nach dem letzten `RouteLeg`) automatisch einen Bremsvorgang zum impliziten End-Halt auf `0` ein.
- Dieser Halt ist nicht als separater Datensatz erforderlich; das RouteControl erkennt das Tabellenende und reagiert automatisch.
- Implementierungsform (v1.2): Befindet sich der Zug im letzten `RouteLeg`, wird die Restdistanz bis zum Tabellenende fortlaufend gegen die erforderliche Bremsdistanz geprüft. Sobald die Restdistanz die Bremsdistanz unterschreitet, plant RouteControl den impliziten End-Halt **genauso früh und regulär** wie ein Bremsziel zu einem `StopPoint`.

### 8.2 Reaktion auf Updates

- Update mit Erhöhung der Zielgeschwindigkeit: laufende Bremsung abbrechen und Beschleunigung neu ansetzen.
- Update mit Reduktion der Zielgeschwindigkeit: sofortige Bremsplanung.
- Replanung passiert unmittelbar im RouteControl.

### 8.3 Routenkonsumierung

- Ein `RouteLeg` gilt erst dann als konsumiert, wenn der **Zugschluss** den `ToWaypointId` des betreffenden `RouteLeg` passiert hat.
- Solange sich der Zugschluss noch innerhalb eines bereits von der Zugspitze verlassenen `RouteLeg` befindet, bleibt dieser `RouteLeg` weiterhin Teil der `RouteTable`.
- Sobald der Zugschluss den Abschnitt vollständig verlassen hat, wird der konsumierte `RouteLeg` atomar aus der `RouteTable` entfernt und `ConsumedRouteCount` erhöht.
- Konsumierte `RouteLeg`-Elemente sind danach nicht mehr adressierbar (insb. kein Replace- oder Remove-Ziel) und können nicht wieder aktiv werden.

### 8.4 StopPoints und Bremsvorlauf

- `StopPoint`-Elemente können an beliebiger Position innerhalb eines `RouteLeg` liegen.
- Der Bremsvorgang wird so geplant, dass am `StopPoint` die Geschwindigkeit `0` erreicht wird.
- Ein definierter `StopPoint` führt immer zu einem Halt; Weiterfahrt erfolgt erst nach `ReleaseGo()`.
- Liegt ein `StopPoint` sehr früh im `RouteLeg`, beginnt der Bremsvorgang bereits im vorausgehenden `RouteLeg`.
- Die Bremsstrecke selbst bleibt dabei erhalten; nur der Bremsbeginn wird nach vorne in die Route verschoben.
- Der Zielabschnitt definiert weiterhin die Bremsweg-Länge; bei frühem `StopPoint` wird der Bremsbeginn um den Abstand zwischen `StopPoint` und `RouteLeg`-Ende in den vorherigen Abschnitt verlagert.
- Implementierungsform (v1.2): Das RouteControl bestimmt den nächsten noch nicht erreichten `StopPoint` leg-übergreifend und schätzt eine erforderliche Bremsdistanz aus aktueller Geschwindigkeit; wird diese Distanz unterschritten, wird bereits im vorherigen `RouteLeg` ein Bremsziel `v = 0` geplant (Vorlauf-Bremsung).
- Aktiviert ein Feedback bereits im Folge-`RouteLeg` (z. B. durch zu schnelle reale Fahrt), darf RouteControl den Positionsabgleich trotzdem annehmen, den laufenden Zyklus sofort beenden und die Fahrt unmittelbar mit dem neu aktiven `RouteLeg` weiterplanen.

## 9. Sicherheitslogik

### 9.1 Impliziter End-Halt und Sicherheitsrückfall

Normalzustand: Das RouteControl plant zum Ende der bekannten Strecke automatisch einen Bremsvorgang auf `0` — ohne dass ein expliziter Halt-Eintrag in der Tabelle erforderlich ist.

Sicherheitsrückfall (`SafetyStopInjected`): Erkennt das RouteControl, dass der Zug die bekannte Strecke überfährt oder das Bremsprofil nicht eingehalten werden kann:
1. `SafetyWarningRaised` emittieren.
2. `SafetyStopInjected = true` setzen.
3. Fahrbefehle so begrenzen, dass am Ende der bekannten Strecke sicher `0` erreicht wird.

### 9.2 Feedback-Timeout und Positionsabweichung

- Wird ein erwarteter Rueckmelder nach Erreichen der rechnerischen Feedback-Position nicht innerhalb `ActivationTimeoutMs` aktiviert:
  1. `FeedbackTimeoutRaised` emittieren,
  2. Sicherheitsstopp auslösen.
- Ziel: Fehlerbild "Zug steht" oder "Zug auf falschem Gleis" sicher abfangen.

### 9.3 Nothalt-Recovery (vorsehen, optional initial)

- Nach Nothalt kann Positionsabweichung vorliegen.
- Nach Freigabe kann RouteControl in `FeedbackInputRecoveryMode` wechseln:
  - Fahrt mit stark reduzierter Geschwindigkeit bis zum nächsten Feedback,
  - danach Normalbetrieb wieder aufnehmen.

## 10. Validierung und Fehler

```csharp
public sealed class RouteValidationException : Exception { }
public sealed class RouteUpdateConflictException : Exception { }
public sealed class RouteStateException : Exception { }
public sealed class FeedbackTimeoutException : Exception { }
```

Zuordnung:
- `RouteValidationException`: Kette/Wegpunkte/Grenzen/Null/leer/Duplikate/ungültige `DistanceCm` oder `MaxSpeedKmh` (≤ 0).
- `RouteUpdateConflictException`: Zielbereich enthält aktiven `RouteLeg`.
- `RouteStateException`: Zielbereich enthält konsumierten `RouteLeg` oder ungültiger Laufzeitzustand.
- `FeedbackTimeoutException`: optional intern, extern primär Event + Sicherheitsstopp.

## 11. Anforderungen an den Builder

Bezug: `OTD/TrainDriving/RouteControl/RouteTableBuilder.cs`

- `AddRoute(...)` muss `maxSpeedKmh` (`> 0`) als Pflichtparameter führen.
- optional `accelerationStartPolicy` aufnehmen.
- `AddStopPoint(...)` soll das nachträgliche Hinzufügen eines eingebetteten `StopPoint` zu einem vorhandenen `RouteLeg` unterstützen.
- Builder-Hilfen für `FeedbackActivationPoints` sind fachlich sinnvoll und sollen die leg-lokale Zuordnung von Rueckmeldern direkt unterstützen.
- Builder bleibt Datensammler; die finale Plausibilität prüft zentral der Validator im RouteControl.

Beispielsignatur:

```csharp
public RouteTableBuilder AddRoute(
    string fromWaypointId,
    string toWaypointId,
    int distanceCm,
    double maxSpeedKmh,
    AccelerationStartPolicy accelerationStartPolicy = AccelerationStartPolicy.AtWaypointCrossing,
    AccelerationTrajectoryPreset? accelerationPreset = null,
    BrakingTrajectoryPreset? brakingPreset = null)
```

## 12. Zielarchitektur (Greenfield)

- `RouteControl/Domain/`
  - `RouteLeg.cs`, `FeedbackHandling.cs`, `RouteActionEvent.cs`, `RoutePositionEvent.cs`, `RouteMetadata.cs`, `AccelerationStartPolicy.cs`
- `RouteControl/Runtime/`
  - `RouteRuntimeState.cs`, `RouteSnapshot.cs`
- `RouteControl/Services/`
  - `RouteTableService.cs` (Add/AddRoutes/ReplaceRoutes/ReplaceRouteAtEnd/RemoveRouteAtEnd/RemoveRoutesFromWaypoint/Advance)
  - `RouteCommandPlanner.cs` (Fahrbefehle aus RouteLeg, impliziter End-Halt)
  - `FeedbackSupervisionService.cs` (Timeouts/Abweichungen)
  - `RouteValidator.cs` (Invarianten, Kontextprüfung)
- `RouteControl/Exceptions/`
  - `RouteValidationException.cs`, `RouteUpdateConflictException.cs`, `RouteStateException.cs`
- `RouteControl/Builder/`
  - `RouteTableBuilder.cs`

Prinzipien:
- Immutable Snapshots nach außen.
- Keine direkten Tabellenmutationen außerhalb des Services.
- Trajectory-Ausführung ist vom RouteControl orchestriert.

## 13. Akzeptanzkriterien v1.2

- Pflichtparameter `DistanceCm` und `MaxSpeedKmh` (`> 0`) werden erzwungen; `DistanceCm <= 0` oder `MaxSpeedKmh <= 0` in einem `RouteLeg` wird mit `RouteValidationException` abgelehnt.
- `FromWaypointId` ist in der aktiven Tabelle eindeutig; doppelte Einträge werden mit `RouteValidationException` abgelehnt.
- `AddRoute`/`AddRoutes` sind append-only; kein Überschreiben bestehender Einträge.
- Einzel- und Mehrfach-Updates (Add/Replace) funktionieren atomar.
- Replace und Remove sind verfügbar; Replace verwendet `legs[0].FromWaypointId` als Startposition, `ReplaceRouteAtEnd` ersetzt den letzten Eintrag, `RemoveRouteAtEnd` entfernt den letzten Eintrag.
- Aktive/konsumierte `RouteLeg`-Elemente sind gegen Replace/Remove gesperrt; Verstoß wirft `RouteUpdateConflictException` bzw. `RouteStateException`.
- RouteControl plant Fahrbefehle nach jedem Add/Replace/Remove sofort neu.
- Am Tabellenende bremst das RouteControl den Zug automatisch auf 0 (impliziter End-Halt), ohne expliziten Halt-Eintrag.
- Der implizite End-Halt wird bereits im letzten `RouteLeg` fahrdynamisch als Bremsziel eingeplant; ein abrupter Geschwindigkeitswechsel erst am Tabellenende ist nicht zulässig.
- Kann das RouteControl den impliziten End-Halt nicht sicher einhalten, emittiert es `SafetyWarningRaised` und setzt `SafetyStopInjected = true`.
- Feedback-Timeout führt zu Event + Sicherheitsstopp.
- FeedbackActivationPoint in `RouteLeg.FeedbackActivationPoints` werden unterstützt; Offset-Validierung erfolgt relativ zur Distanz des zugehörigen `RouteLeg`.
- `OnFeedbackInputActivated(feedbackId)` nutzt FeedbackActivationPoint zur Positionsrekalibrierung und triggert sofortige Fahrstrategie-Neuberechnung.
- StopPoints innerhalb eines `RouteLeg` werden unterstützt; bei frühem `StopPoint` beginnt der Bremsvorgang bereits im vorausgehenden `RouteLeg`.
- Pro `RouteLeg` ist maximal ein `StopPoint` zulässig.
- Ein definierter `StopPoint` führt immer zu einem Halt (`v = 0`).
- Weiterfahrt nach `StopPoint` erfolgt ausschließlich über `ReleaseGo()`.
- Die Bremsweg-Länge wird aus dem Zielabschnitt abgeleitet; nur der Bremsbeginn wird bei frühem `StopPoint` vorverlegt.
- Die Bindung an `Train` erfolgt über den `RouteController`-Konstruktor; `BoundTrain` exponiert grundlegende Zugfunktionen.
- Nothalt-Recovery ist im Modell vorgesehen (mindestens als Zustand/Hook).
- **InitialHold**: Wird der RouteController mit `initialHold: true` erstellt, fährt der Zug nach dem ersten `AddRoute`/`ReplaceRoutes`-Aufruf nicht sofort los; erst nach `ReleaseGo()` setzt sich der Zug in Bewegung. Der InitialHold ist einmalig; danach wirkt `ReleaseGo()` ausschliesslich auf `StopPoint`-Halte.

## 14. Offene Punkte (Implementierungs-Backlog)

- `RouteActionEvent` und `RoutePositionEvent` sind im Domänenmodell der Spec definiert, aber in `RouteControl` derzeit noch nicht als Runtime-Ausführungspfad integriert.
- Externe Event-Pipeline gemäss `RouteControlEvent` (`SafetyWarningRaised`, `SafetyStopInjected`, `FeedbackTimeoutRaised`) ist noch nicht vollständig als publizierte API umgesetzt.
- `FeedbackSupervisionService` mit echtem `ActivationTimeoutMs`-Handling (Timeout-Erkennung + Eventauslösung) ist als Architekturziel definiert, aktuell jedoch noch nicht als separater Service implementiert.
- Feedback-Rekalibrierung benötigt ein explizites Korrekturfenster (z. B. ±15 cm um die erwartete Marker-Position) mit klarer Accept/Reject-Policy; bei akzeptierter Korrektur muss die Bremsrampe/Fahrstrategie unmittelbar neu geplant werden.
- `RouteCommandPlanner` als eigenständiger Service ist architektonisch vorgesehen; die Fahrplanungslogik liegt aktuell überwiegend direkt im `RouteController`.
- `RouteTableBuilder` unterstützt aktuell `AddRoute(...)` und `AddStopPoint(...)`; explizite Builder-Hilfsmethoden für `FeedbackActivationPoints` sind noch nicht umgesetzt.
- **[SICHERHEIT]** Unerwartete Rueckmelder: Wird waehrend einer Fahrt ein Feedback aktiv, das aufgrund der Sequenz der RouteLegs nicht erwartet wird (d. h. kein entsprechender `FeedbackActivationPoint` im aktiven oder naechsten RouteLeg vorhanden ist), ist dies ein Indiz fuer einen Fehlerbetrieb (z. B. Zug auf falscher Route, Feedback-Fehlfunktion, Gleiswechsel-Fehler). In diesem Fall sollte RouteControl mit `FeedbackUnexpectedWarning` ein Sicherheitsereignis emittieren, einen notbremsaehnlichen Halt einleiten und den Fehlerfall mit `RouteUnexpectedFeedbackException` dokumentieren. Das System wartet danach auf explizite Freigabe durch den Benutzer bzw. das Stellwerk (`OnEmergencyRelease()`).


