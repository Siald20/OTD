# TrainDriving

Dieses Modul steuert die Geschwindigkeitsfuehrung eines Zuges entlang einer Route.

## Leitidee

- Die Fahrerlaubnis gilt am **Start-Waypoint** einer Route.
- Jede Route ist ein Abschnitt `FromWaypointId -> ToWaypointId` mit Distanz und `StartRoutePermission`.
- Ohne Folgeroute gilt am Ende fail-safe **Halt (`0 km/h`)**.
- Beschleunigen ist **startorientiert**: `AccelerationTrajectoryPreset` formt die Kurve, `TrainDriving.AccelerationMs2` setzt die absolute Beschleunigung in `m/s²`.
- Bremsen bleibt **zielorientiert**: `BrakingTrajectoryPreset` passt den Verlauf zum Zielpunkt an.

## Bausteine

- `TrainDriving`: erzeugt Trajektorien und sendet zyklisch Sollgeschwindigkeit an `Train`.
- `RouteModel.RouteTable`: in-memory Route mit Waypoint-Ankern, Sensorankern und Actions.
- `RouteModel.RouteRuntime`: kombiniert Distanzintegration, optionale Sensor-Rekalibrierung und Speed-Begrenzung.
- `Trajectory.*`: parametrisierte Geschwindigkeitskurven.

## Schnellstart

```csharp
using OTD.TrainDriving;
using OTD.TrainDriving.RouteModel;

var table = new RouteTableBuilder()
    .AddRoute(
        id: 1,
        fromWaypointId: "S1",
        toWaypointId: "S2",
        distanceCm: 120,
        startRoutePermission: RoutePermission.Proceed(maxSpeedKmh: 40, aspect: "Fahrt"))
    .AddRoute(
        id: 2,
        fromWaypointId: "S2",
        toWaypointId: "S3",
        distanceCm: 180,
        startRoutePermission: RoutePermission.Stop())
    .Build();

var runtime = new RouteRuntime(table);
var driving = new TrainDriving(train)
{
    AccelerationPreset = AccelerationTrajectoryPreset.Comfort,
    AccelerationMs2 = 0.55,
    BrakingPreset = BrakingTrajectoryPreset.LateBrake,
    LookAheadFactor = 1.20,
    LookAheadDistanceCm = 12.0,
    LookAheadSpeedCompensationCmPerKmh = 0.12
};

var tick = runtime.ApplyStep(deltaCm: 10, trajectorySpeedKmh: 60);
if (tick.ActiveCycle is { } cycle)
{
    await driving.DriveRouteCycleAsync(
        currentSpeedKmhPrototype: 60,
        cycle: cycle,
        cancellationToken: token);
}
```

## Direkter Fahrbefehl (ohne RouteRuntime)

```csharp
await driving.DriveAsync(
    currentSpeed: 0,
    targetSpeed: 40,
    distance: 200,
    cancellationToken: token);
```

## RouteRuntime-Regel fuer Effective Speed

- `requested = trajectorySpeedKmh`
- `allowed = permission.MaxSpeedKmh` (oder `0` bei Halt)
- `effective = min(requested, allowed)`

## Hinweise

- `RouteCycle.AllowedSpeedKmh` kommt aus der Permission am Start-Waypoint des Zyklus.
- `RouteDriveProfile` traegt nur Presets (`AccelerationPreset`, `BrakingPreset`).
- Standardwert fuer `AccelerationMs2` ist aktuell `0.55 m/s²`.
- Standardwert fuer `LookAheadFactor` ist `1.0` (0.0 deaktiviert Look-Ahead, >1.0 sendet frueher).
- `LookAheadDistanceCm` ist der feste Preview-Weg in Modell-cm.
- `LookAheadSpeedCompensationCmPerKmh` vergroessert den Preview-Weg bei hoeherer Geschwindigkeit.
- Intern wird fuer die Distanzberechnung auf den Modellmassstab umgerechnet.
- `SensorMarker.SensorId` entspricht direkt der `SensorNumber` aus `Feedback.SensorStateChanged`.
- Es gibt keinen `CruiseSpeedKmhPrototype`-Pfad mehr.

## Reminder

- Langfristig soll `AccelerationMs2` nicht nur manuell gesetzt werden, sondern automatisch aus Daten in `Train` bzw. `TrainComposition` abgeleitet werden, z. B. aus Betriebsmasse, Motorleistung und Anzahl angetriebener Fahrzeuge.

## TODO

- **Unit-System-Abstraction (Metric/Imperial)**: Trajectory und DrivingTrajectoryRequest entkoppeln von hardcodierten Einheiten (km/h, cm). Dies soll zusammen mit der RoutingModel-Finalisierung durchgeführt werden:
  - `IUnitSystem`-Interface mit Implementierungen `MetricUnitSystem` (km/h → cm) und `ImperialUnitSystem` (mph → inch)
  - Konversionsfaktor: Metric `1 km/h = 100000/3600 cm/s`, Imperial `1 mph = 63360/3600 inch/s`
  - `DrivingTrajectoryRequest` um `UnitSystem`-Parameter erweitern
  - `ISpeedTrajectory` generalisieren: `TotalDistanceCmModel` → `TotalDistanceModel`, `GetSpeedKmhAtModelDistanceCm()` → `GetSpeedPrototypeAtModelDistance()`
  - `ModelCmPerSecondFromPrototypeKmh()` durch generalisierte Methode ersetzen
  - `SpeedCurveFidelityPercent` bereits vorbereitet (nicht mehr nur auf Bremsen begrenzt)
- Verfallene `RouteEntry`-Eintraege und ihre `SensorMarker` aus der aktiven `RouteTable` entfernen, sobald die Folgeroute erreicht wurde.
