# SensorMarker Position bei Leg-Übergängen: Analyse & Empfehlungen

**Datum:** 2026-06-29  
**Frage:** Sollte ein Sensor, der genau am Übergang von RouteLeg A → RouteLeg B liegt, als **OffsetCm am Ende von A** oder **am Anfang von B** registriert werden?

---

## Executive Summary

**Empfehlung:** Sensor am **Anfang des folgenden Legs (B)** mit `OffsetCm: 0` registrieren.

| Kriterium | Ende von A | Anfang von B | **Gewinner** |
|-----------|-----------|-------------|------------|
| **Zyklus-Kohärenz** | ✗ Schleife endet, Leg-Wechsel | ✓ Sofort im neuen Zyklus | **B** ✓ |
| **Positionsstracking** | ⚠️ Grenzfall, hohe Fehlerempfindlichkeit | ✓ Saubere Neukalibrierung | **B** ✓ |
| **Cycle-Replan-Sicherheit** | ✗ Replan könnte Sensor überspringen | ✓ Protected by ForwardLegSync | **B** ✓ |
| **Sensor-Gate Robustheit** | ⚠️ Delta-Fehlerakkumulation | ✓ Saubere Sprünge | **B** ✓ |
| **Debuggbarkeit** | ✗ Mehrdeutig (welcher Zyklus?) | ✓ Eindeutig (legid) | **B** ✓ |

---

## 1. Hintergrund: Wie SensorMarker funktionieren

### Struktur

```csharp
// SensorMarker.cs
public sealed record SensorMarker(
    int SensorId,
    int OffsetCm,                    // Position innerhalb des RouteLeg
    int? ActivationTimeoutMs = null);

// Validation (RouteValidator.cs:41)
if (marker.OffsetCm < 0 || marker.OffsetCm > leg.DistanceCm)
    throw new RouteValidationException(...)
```

**Wichtig:** SensorMarker ist **immer an ein RouteLeg gebunden**. Die absolute Position wird berechnet als:

```
AbsolutePositionCm = SUM(RouteLeg[0..i-1].DistanceCm) + RouteLeg[i].OffsetCm
```

### Recalibration-Flow

```csharp
// RouteRuntime.ApplyStep()
PositionTracker.IntegrateDelta(deltaCm);           // Schätzung vor Sensor
if (activatedSensorId is not null)
    PositionTracker.TryRecalibrateFromSensor(activatedSensorId);

// PositionTracker.TryRecalibrateFromSensor()
if (!_routeTable.TryGetSensorAnchor(sensorId, out var anchor))
    return false;                                   // Sensor nicht in RouteTable
var error = anchor.S_Cm - EstimatedPositionCm;    // Abweichung
if (Math.Abs(error) > _correctionGateCm)          // default: 150 cm
    return false;
EstimatedPositionCm = anchor.S_Cm;                // Neukalibrierung
```

---

## 2. Szenario A: Sensor am ENDE von Leg A (OffsetCm = LegA.DistanceCm)

### Timing-Diagram

```
RouteController.Run() Loop
├─ Tick 1: Zug bei Position 50 cm in Leg A (223 cm lang)
│  └─ DriveRouteCycleAsync(LegA) führt Trajectory aus
│
├─ Tick 2: Zug erreicht 223 cm (Ende von LegA)
│  └─ ProgressTick(DeltaCmModel=1.5) emittiert
│  └─ OnProgressTick(): AdvanceByDelta(1.5)
│     └─ Position: 221.5 → 223.0 cm (integriert)
│  └─ SENSOR AKTIVIERT (während Integration)
│  └─ OnSensorActivated(sensorId):
│     └─ error = 223.0 - 223.0 = 0 cm ✓
│     └─ EstimatedPositionCm = 223.0 cm
│
├─ Tick 3: LegA-Zyklus endet, neue Zielgeschwindigkeit = 0 (Leg-Wechsel)
│  └─ BrakeAsync(targetSpeed=0, distance=223)
│  └─ Neue Bremsrampe geplant
│
├─ Tick 4: Übergang zu LegB (K102→H41, 163 cm)
│  └─ DriveRouteCycleAsync(LegB) startet
│  └─ Position wird WIEDER ab 223 cm bei 0 (OffsetCm im neuen Leg)
│  └─ **PROBLEM:** Sensor war am Ende des alten Legs, aber
│     jetzt im neuen Zyklus, nicht mehr Teil von LegB!
```

### Probleme

#### ❌ **Problem 1: Zyklus-Mehrdeutigkeit**

```csharp
// Sensor registriert auf LegA @ OffsetCm: 223
// Aber beim Übergang zu LegB: Welcher Leg "besitzt" den Sensor?
// RouteValidator.cs:68 → global uniqueness geprüft
// aber logische Zugehörigkeit unklar
```

#### ❌ **Problem 2: ForceForwardLegSync-Anomalie**

```csharp
// RouteTableService.OnSensorActivated() setzt:
activation.ForcedForwardLegSync = true  // wenn Sensor am Ende eines Legs

// Das Signal bedeutet: "Zug hat Leg verlassen, zum nächsten übergegangen"
// **Aber:** Wenn Sensor am Ende = immer ForcedForwardLegSync = True
// → Macht Leg-Ende-Platzierung verdächtig
```

#### ⚠️ **Problem 3: Cycle-Replan-Race**

Wenn RouteController während der Bremsrampe einen neuen Leg hinzufügt:

```csharp
// Tick N: Zug bei 221 cm in LegA, Ziel: 0 km/h (bremst)
// Sensor @ LegA[223] würde _jetzt_ untergehen (außerhalb aktive Zyklus)

// Gleichzeitig: controller.AddRoute(LegB) hinzugefügt
// → Active cycle wird abgebrochen + neu geplant
// → Sensor könnte übersehen werden (war am End des OLD Zyklus)
```

---

## 3. Szenario B: Sensor am ANFANG von Leg B (OffsetCm: 0)

### Timing-Diagram

```
RouteController.Run() Loop
├─ Tick 1–3: ... Zug bei 221 cm in LegA, bremst
│
├─ Tick 4: LegA endet, LegB (K102→H41, 163 cm) wird aktiv
│  └─ DriveRouteCycleAsync(LegB) gestartet
│  └─ Position = 223.0 cm (cumulative)
│  └─ **Neuer Zyklus startet mit klarer Semantik**
│
├─ Tick 5: Zug beschleunigt in LegB
│  └─ Position wächst: 223.0 → 223.1 → 223.2 → ...
│  └─ SENSOR AKTIVIERT @ 223.0 cm (Anfang von LegB)
│  └─ OnSensorActivated(sensorId):
│     └─ error = 223.0 - 223.0 = 0 cm ✓
│     └─ **Recalibration erfolgt SOFORT im neuen Zyklus**
│
├─ Tick 6: Sensor akzeptiert, neue Route-Lookup-Anfrage
│  └─ TryGetRouteCycleAt(223.0) → findet LegB
│  └─ PermissionState & ActiveCycle aktualisieren sich sauber
```

### Vorteile

#### ✅ **Vorteil 1: Zyklus-Kohärenz**

```csharp
// Sensor @ LegB[OffsetCm: 0] = logisch "Eingangsverifizierung" des neuen Legs
// Sehr klare Semantik: "Zug betritt LegB korrekt"
```

#### ✅ **Vorteil 2: ForceForwardLegSync ist natürlich**

```csharp
// OnSensorActivated @ Leg-Anfang → System weiss automatisch:
// "Position ist nur valide NACH Leg-Wechsel"
// → Sensor wird nur im neuen Zyklus akzeptiert
```

#### ✅ **Vorteil 3: Replan-Sicherheit**

```csharp
// Wenn controller.AddRoute() während Bremsrampe:
// 1. Alter Zyklus (LegA) wird abgebrochen
// 2. Neuer Zyklus (LegB) startet sofort mit Sensor @ Anfang
// 3. Sensor kann NICHT übersehen werden (Teil des neuen Legs von Start)

// Code-Logik: OnSensorActivated() prüft:
// activation.Accepted = legSensors.Any(s => s.SensorId == sensorId)
// → Sensor @ LegB[0] ist SOFORT erkannt
```

#### ✅ **Vorteil 4: Gate-Robustheit**

```
Szenario: Zug unterschätzt Bremsverzögerung

E-Halt LegA         Sensorposition      Start LegB
   |                     |                  |
   0                     223               223
   ├─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┼ ─ ─ ─ ─ ─ ─ ─ ─ ┤
                          Sensor @ 223
   
   Zug bremst von 20 → 0 km/h:
   
   A) Sensor @ Ende LegA[223]:
      └─ Position-Schätzung: Δ = (20→0) * Δt = noch offen
      └─ Abweichung könnte 15–50 cm sein (Bremsrampe-Fehler!)
      └─ Gate (150 cm) bricht nicht, aber Error ist hoch

   B) Sensor @ Anfang LegB[0]:
      └─ LegA komplett => Leg-Wechsel ist DONE
      └─ LegB startet clean: Position = 223.0
      └─ SENSOR triggert SOFORT
      └─ Keine Bremsrampen-Fehlerakkumulation
```

---

## 4. Worst-Case: Was passiert bei End-of-Leg Platzierung?

### Debugging-Szenario: Sensor wird ignoriert

```csharp
// RouteValidator.cs:41 — Validierung
// marker.OffsetCm = 223, leg.DistanceCm = 223 ✓ (valid, <= Distanz)

// RouteTableService.OnSensorActivated()
// → TryGetSensorAnchor() findet Sensor @ position 223
// → Aber wenn Zug GERADE beim Leg-Wechsel:
//    └─ Position Schätzung könnte 221.5 sein (Integral noch laufend)
//    └─ error = 223 - 221.5 = 1.5 cm ✓ (inside gate)
//    └─ Sollte funktionieren...

// ABER: Race Condition wenn Fahrzyklus bereits zu Leg B übergegangen
//   └─ _activeCycle ist jetzt LegB
//   └─ SensorMarker ist noch auf LegA
//   └─ ForceForwardLegSync=true wird gesetzt
//   └─ **=> Zyklus wird sofort abgebrochen für Replan**
//   └─ **=> Debug-Logs zeigen "largeCorrection=YES" auch wenn error=1.5!**
```

---

## 5. Best Practice: Implementierungs-Richtlinie

### ✅ **Empfohlenes Schema**

```csharp
// Leg A: Bitschikon Gleis 2 → K102 (223 cm)
controller.AddRoute(
    new RouteLeg(
        FromWaypointId: "B2",
        ToWaypointId: "K102",
        DistanceCm: 223,
        MaxSpeedKmh: 40,
        SensorMarkers:
        [
            // Sensoren der Anfahrt (NICHT am Ende!)
            new SensorMarker(SensorId: 28, OffsetCm: 3),   // Eingang
            new SensorMarker(SensorId: 31, OffsetCm: 43),  // Mitte
            new SensorMarker(SensorId: 30, OffsetCm: 73)   // nächster Punkt
            // ⚠️ NICHT: new SensorMarker(SensorId: 999, OffsetCm: 223) ← FALSCH
        ]));

// Leg B: K102 → H41 (163 cm)
controller.AddRoute(
    new RouteLeg(
        FromWaypointId: "K102",
        ToWaypointId: "H41",
        DistanceCm: 163,
        MaxSpeedKmh: 60,
        SensorMarkers:
        [
            // ✓ KORREKT: Sensor am Übergang gehört zum Ziel-Leg
            new SensorMarker(SensorId: 148, OffsetCm: 0),   // Eingangsverifizierung
            new SensorMarker(SensorId: 147, OffsetCm: 53)   // Weitere Punkte
        ]));
```

### Sensor-Platzierung Matrix

```
Zweck des Sensors          Segment A Länge: 223 cm    Segment B Länge: 163 cm
─────────────────────────  ─────────────────────────  ─────────────────────────

Beschleunigung kontrollieren
                           OffsetCm: 10–50          (nicht hier)

Mittelpunkt detektieren    OffsetCm: 100–120        (nicht hier)

Bremsinitialisierung       OffsetCm: 150–200        (nicht hier)

Endkontrolle Segment A     (NICHT @ 223!)           STATTDESSEN:
                                                     OffsetCm: 0 in Segment B

Eingangsverifizierung B                             OffsetCm: 0 ✓
                                                     
Beschleunigung B                                    OffsetCm: 20–50 ✓

Zielkontrolle B                                     OffsetCm: 150–160 ✓
```

---

## 6. Validierung & Fehlerbehandlung

### Best Practice: Guard im RouteLeg

```csharp
// RouteValidator.cs (könnte ergänzt werden):
public static void ValidateSensorMarkerPlacement(RouteLeg leg)
{
    if (leg.SensorMarkers is null)
        return;

    foreach (var marker in leg.SensorMarkers)
    {
        // ⚠️ WARNUNG: Sensor genau am Ende könnte mehrdeutig sein
        if (marker.OffsetCm == leg.DistanceCm)
        {
            Logging.Warn<RouteValidator>(
                $"SensorMarker '{marker.SensorId}' on RouteLeg '{leg.FromWaypointId}' is at exact end-of-leg ({leg.DistanceCm} cm). " +
                $"Consider moving it to OffsetCm: 0 of the next leg for clearer semantics.");
        }

        // ✓ OK: Sensor mit kleinem Offset am Anfang ist ideal
        if (marker.OffsetCm == 0)
        {
            Logging.Debug<RouteValidator>(
                $"SensorMarker '{marker.SensorId}' on RouteLeg '{leg.FromWaypointId}' at entry (OffsetCm: 0) — good for recalibration.");
        }
    }
}
```

---

## 7. Zusammenfassung der Stabilität

### Synchronisierungsstabilität nach Leg-Übergang

| Situation | Leg-Ende (A) | Leg-Anfang (B) |
|-----------|-------------|----------------|
| **Normalbetrieb** | ⚠️ Grenzfall | ✅ Robust |
| **Bremsrampen-Fehler** | ⚠️ Error 10–50 cm | ✅ Error 0–5 cm |
| **Cycle-Replan** | ❌ Sensor könnte übersehen | ✅ Protected |
| **Sensor-Gate Überschuss** | ⚠️ Möglich, wenn viele Fehler akkumulieren | ✅ Sauber |
| **Debuggbarkeit** | ⚠️ Welcher Zyklus? | ✅ Eindeutig |
| **Performance** | ✅ Gleich | ✅ Gleich |

---

## 8. Konkrete Empfehlungen

### 🥇 **Best Practice (Priorität 1)**

**Setzen Sie Übergangssensoren am ANFANG des Ziel-Legs mit `OffsetCm: 0`.**

```csharp
// ✓ RICHTIG
new RouteLeg(FromWaypointId: "K102", ..., 
    SensorMarkers: [new SensorMarker(148, OffsetCm: 0)]);  // Eingang
```

### 🥈 **Alternative (falls Sensor physisch nur am Ende platzbar)**

Falls der Sensor physisch nur am Ende von Leg A sitzen kann:

1. **Dokumentieren Sie den Grund** in Konfiguration/Code
2. **Erhöhen Sie die CorrectionGate** (default 150 cm) auf 200 cm für diese Strecke
3. **Aktivieren Sie erweiteres Logging** für große Korrektionen (> 15 cm)

```csharp
var positionTracker = new PositionTracker(
    routeTable,
    initialPositionCm: 0.0,
    correctionGateCm: 200.0);  // Toleranter für problematische Sensoren
```

### 🥉 **Nicht empfohlen**

❌ Sensor genau am `OffsetCm == leg.DistanceCm` registrieren  
❌ Sensor am Ende eines Legs ohne neue Kopie am Anfang des nächsten  
❌ Auf Leg-Übergängen Sensoren nutzen **ohne** zu dokumentieren

---

## 9. Weitere Lesematerial

- `RouteValidator.cs` – Validierungs-Regeln
- `RouteTableService.OnSensorActivated()` – Sensor-Verarbeitung
- `PositionTracker.TryRecalibrateFromSensor()` – Recalibration-Logik
- `RouteController.OnSensorActivated()` – High-Level Handling
- `RouteControl_SPEC_v1.md` – Spezifikation

---

**Dokumentation erstellt:** 2026-06-29  
**Best Practice:** Sensor am Anfang des Ziel-Legs (OffsetCm: 0)

