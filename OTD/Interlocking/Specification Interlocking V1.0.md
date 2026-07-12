# Specification Interlocking V1.0

Status: Draft V1.2  
Kontext: `OTD/Interlocking/`  
Architekturmodus: Backend-only (ILTIS ist ausserhalb des aktuellen Umfangs)

---

## 1. Ziel

Diese Spezifikation beschreibt ausschliesslich den Funktionsumfang von **Interlocking** als Backend fuer Stellwerks- und Zusteuerungslogik.

Nicht Teil dieses Dokuments:

- ILTIS-Frontend
- UI-/Bedienkonzepte
- Zug- und Fahrplanverwaltung auf Frontend-Seite

Interlocking ist die autoritative Instanz fuer Fahrstrassen-, Accessory- und Sicherheitsentscheidungen.

---

## 2. Architekturkontext

```
OTD
├── Interlocking/                   <- Fokus dieses Dokuments
│   ├── Services/
│   │   ├── InterlockingController.cs
│   │   ├── RouteProvisionService.cs
│   │   ├── AccessoryOrchestrator.cs
│   │   ├── OccupancyService.cs
│   │   └── SafetySupervisor.cs
│   ├── Integra/
│   ├── Relais/
│   └── Global.cs
├── TrainDriving/
│   └── RouteControl/
└── HardwareControl/
    ├── CommandStation/
    ├── Accessory/
    └── Feedback/
```

### Abhaengigkeiten

- `RouteController` (RouteTable-Pflege, `ReleaseGo`, `RouteSnapshot`)
- `ICommandStation`/Accessory-Treiber (Weichen- und Signalbefehle)
- Feedback-System (Occupancy-/Kontakt-Eingaben)
- `Global.g_cycle_time_ms` fuer zyklische Auswertung

---

## 3. Verantwortlichkeiten von Interlocking

1. **Fahrstrassenverwaltung**
   - Fahrweg anfordern, validieren, aufbauen, aendern, aufloesen
   - RouteLeg-Folgen gegenueber `RouteController` pflegen

2. **Accessory-Orchestrierung**
   - Weichen stellen
   - Signale schalten
   - Rueckmeldungen zu Stellvorgaengen auswerten

3. **Fahrwegfreigabe**
   - `ReleaseGo()` nur nach erfolgreicher Sicherungspruefung
   - Freigaben fuer InitialHold/StopPoint steuerbar

4. **Belegungs- und Rueckmeldeverarbeitung**
   - Occupancy-Modell fuehren
   - Kontakt- und Occupancy-Events verarbeiten
   - mit Route-Laufzeitzustand synchronisieren

5. **Sicherheitslogik**
   - Konflikte verhindern (belegte Gleise, unbestaetigte Weichenlage)
   - Sicherheitsstopp behandeln
   - Signale auf Halt bei Gefaehrdung

---

## 4. Accessory-Steuerung (Weichen/Signale)

Die Accessory-Ansteuerung ist integraler Teil von Interlocking.

### 4.1 Grundregeln

- Accessory-Befehle laufen ausschliesslich ueber `ICommandStation`.
- Mehrfach-Decoder-Zustaende werden sequenziell in XML-Reihenfolge geschaltet.
- Rueckmeldung mit `interlocking="Bi"` ist fuer Freigabeentscheidungen verpflichtend.
- Bei Timeout: Fahrstrasse sperren, Signal auf Halt, Fehlerzustand ausloesen.

### 4.2 Weichensteuerung

- Zielzustand wird aus der Fahrstrasse abgeleitet.
- Weiche ist waehrend Stellvorgang gesperrt.
- Stellungswechsel in belegtem Konfliktbereich wird abgelehnt.

### 4.3 Signalsteuerung

- Startup: alle Signale auf `Halt`.
- Vor `ReleaseGo()`: nur bei bestaetigter Weichenlage und freiem Fahrweg.
- Bei Fahrstrassenaufloesung oder Sicherheitsstopp: sofortige Ruecknahme auf `Halt`.

---

## 5. Interlocking-API (fachlich)

```csharp
public interface IInterlockingFacade
{
    Task RequestRouteAsync(string trainUid, string fromWaypointId, string toWaypointId, CancellationToken ct = default);
    Task CancelRouteAsync(string trainUid, CancellationToken ct = default);
    Task ReleaseDepartureAsync(string trainUid, CancellationToken ct = default);

    Task SetAccessoryStateAsync(string accessoryUid, string stateName, CancellationToken ct = default);

    RouteSnapshot? GetRouteSnapshot(string trainUid);
    IReadOnlyDictionary<string, bool> GetOccupancy();
    IReadOnlyList<AccessoryRuntimeState> GetAccessoryStates();
}
```

Hinweis: Der konkrete API-Client (z. B. ILTIS) ist ausserhalb dieses Dokuments.

### 5.1 Ereignisse (Interlocking-seitig)

Interlocking publiziert mindestens:

- `RouteStateChanged`
- `AccessoryStateChanged`
- `OccupancyChanged`
- `SafetyWarningRaised`
- `SafetyStopInjected`

### 5.2 Transport

V1.2: in-process Adapter ist zulaessig.  
Optional spaeter: REST/HTTP oder WebSocket.

---

## 6. Invarianten

1. Sicherheitskritische Entscheidungen erfolgen nur im Interlocking.
2. `RouteController`-Mutationen (`AddRoutes`, `ReplaceRoutes`, `Remove...`) erfolgen nur ueber Interlocking-Orchestrierung.
3. Accessory-Steuerung erfolgt nur ueber Interlocking-Orchestrierung.
4. Freigabe ohne bestaetigte Weichen-/Fahrwegvoraussetzungen ist unzulaessig.
5. Bei Sicherheitsstopp muessen betroffene Signale auf `Halt` gesetzt werden.

---

## 7. Fehlerbehandlung

```csharp
public sealed class InterlockingRouteConflictException : Exception { }
public sealed class InterlockingAccessoryTimeoutException : Exception { }
public sealed class InterlockingSafetyLockException : Exception { }
```

- Fehler entstehen im Interlocking und werden als fachliche Fehler propagiert.
- Sicherheitsfehler fuehren immer zu einem deterministischen Fail-Safe-Zustand.

---

## 8. Akzeptanzkriterien

- Interlocking kann Fahrstrassen ohne frontend-seitige Entscheidungslogik aufbauen und absichern.
- Weichen und Signale werden ausschliesslich durch Interlocking geschaltet.
- `ReleaseGo()` wird nur bei erfuellten Sicherheitsbedingungen ausgeloest.
- Occupancy- und Kontaktmeldungen aktualisieren den Interlocking-Zustand konsistent.
- Sicherheitsereignisse werden erzeugt und in einen sicheren Anlagenzustand ueberfuehrt.

---

## 9. Backlog (Interlocking)

- verfeinerte Konfliktloesung bei konkurrierenden Fahrstrassen
- konfigurierbare Timeouts pro Accessory-Typ
- erweiterte Diagnose fuer Decoder-Sequenzen und Rueckmeldetimeouts
- optionaler Netzwerk-Transport (REST/WebSocket) fuer externe Clients
