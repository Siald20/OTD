# TrainDriving

Dieses Modul steuert die Geschwindigkeitsfuehrung eines Zuges entlang eines RouteControl-Fahrwegs.

## Leitidee

- Die Fahrt basiert auf geordneten `RouteLeg`-Abschnitten (`FromWaypointId -> ToWaypointId`).
- Jeder Abschnitt definiert Distanz und `MaxSpeedKmh`.
- Ohne Folgeroute gilt am Tabellenende fail-safe Halt (`0 km/h`).
- Beschleunigung und Bremsung werden ueber `TrainDriving`-Trajektorien gesteuert.

## Bausteine

- `TrainDriving`: erzeugt Trajektorien und sendet zyklisch Sollgeschwindigkeit an `Train`.
- `RouteControl.Services.RouteTableService`: verwaltet `RouteLeg`-Tabelle + Runtime-State.
- `RouteController`: orchestriert Fahrzyklen, Re-Planning und StopPoint-/Rueckmelde-Logik.
- `Trajectory.*`: parametrisierte Geschwindigkeitskurven.

## Schnellstart

```csharp
using OTD.TrainDriving;
using OTD.TrainDriving.RouteControl.Builder;

var routeLegs = new RouteTableBuilder()
    .AddRoute(
        fromWaypointId: "S1",
        toWaypointId: "S2",
        distanceCm: 120,
        maxSpeedKmh: 40)
    .AddRoute(
        fromWaypointId: "S2",
        toWaypointId: "S3",
        distanceCm: 180,
        maxSpeedKmh: 30)
    .AddStopPoint(fromWaypointId: "S2", offsetCm: 80, stopReason: "Bahnsteig")
    .Build();

var controller = new RouteController(train, initialHold: true)
{
    AccelerationMs2 = 0.55
};

controller.AddRoutes(routeLegs);
controller.ReleaseGo();
await controller.Run(token);
```

## Direkter Fahrbefehl (ohne RouteController)

```csharp
await driving.DriveAsync(
    currentSpeed: 0,
    targetSpeed: 40,
    distance: 200,
    cancellationToken: token);
```

## Hinweise

- `StopPoint` erzwingt Halt; Weiterfahrt erfolgt via `ReleaseGo()`.
- Rueckmeldeabgleich erfolgt ueber `RouteController.OnFeedbackInputActivated(int feedbackId)`.
- `AccelerationStartPolicy` steuert, wann bei schnellerem Folge-Leg beschleunigt wird.
- `RouteControl_SPEC_v1` liegt unter `TrainDriving/RouteControl/RouteControl_SPEC_v1.md`.

## Architektur & Optimierung

**Frage: Redundanzen zwischen Trajectory und PositionTracker?**

Siehe [`OPTIMIZATION_ANALYSIS.md`](./OPTIMIZATION_ANALYSIS.md) für eine detaillierte Analyse:
- ✅ Redundanzanalyse: Keine substantielle Redundanz
- ✅ Performance-Baseline: Beide Systeme sind CPU-effizient
- 💡 Konkrete Optimierungspotenziale (3 Prioritäten)
- 🎯 Architektur-Schlussfolgerung: Status quo ist optimal

**TL;DR:** Trajectory und PositionTracker arbeiten auf orthogonalen Abstraktionsebenen (Low-Level Kurven vs. High-Level Route-State). Eine Konsolidierung würde Komplexität erhöhen ohne Gewinn.

## TODO

- **Unit-System-Abstraction (Metric/Imperial)**: Trajectory und DrivingTrajectoryRequest entkoppeln von hardcodierten Einheiten (km/h, cm):
  - `IUnitSystem`-Interface mit Implementierungen `MetricUnitSystem` und `ImperialUnitSystem`
  - `DrivingTrajectoryRequest` um `UnitSystem`-Parameter erweitern
  - `ISpeedTrajectory` auf einheitenneutrale Modellstrecke generalisieren
  - interne Umrechnungsmethoden entsprechend abstrahieren
