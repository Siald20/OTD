# TrainDriving

Dieses Modul implementiert master-seitige Zugsteuerung durch **wegbasierte Geschwindigkeitstrajektorien**.

## Architektur

- **TrainDriving (Master)**: berechnet Sollwerte entlang der Strecke
- **Train (Slave)**: fuehrt nur Fahrbefehle aus (`SetSpeedVAsync(...)`)
- **DecoderSpeedResponseModel**: bildet den zeitlichen Decoder-Nachlauf auf Sollwerte ab

## Parametrische Trajektorien

Trajektorien werden ausschliesslich ueber Werte im `DrivingTrajectoryRequest` beschrieben:

- `CurveType`: `Linear`, `ControlPoint`, `EaseInOut`
- `ControlPoint`: optionaler Stuetspunkt `(XModelRatio, SpeedKmhPrototype)`
- `CurveShapePercent`: Formparameter fuer `EaseInOut` (`0..100`)

### Beispiel: Freie Kurvenbiegung mit Stuetspunkt

```csharp
var request = new DrivingTrajectoryRequest(
    CurrentSpeedKmhPrototype: 20,
    TargetSpeedKmhPrototype: 80,
    DistanceCmModel: 160,
    Scale: 87,
    CurveType: TrajectoryCurveType.ControlPoint,
    ControlPoint: new TrajectoryControlPoint(0.40, 60));

ISpeedTrajectory trajectory = new ParametricTrajectory(request);
```

## Vordefinierte Werte-Kombinationen (Presets)

Presets sind nach Fahrtrichtung getrennt:

**`AccelerationTrajectoryPreset`** – fuer Beschleunigungsphasen:
- `Linear` – konstante Beschleunigung
- `Comfort` – sanfte S-Kurve
- `EarlyAcceleration` – fruehes, kraeftiges Beschleunigen
- `LateAcceleration` – spaeter Antritt
- `BalancedControlPoint` – symmetrischer Mittelpunkt

**`BrakingTrajectoryPreset`** – fuer Bremsenphasen:
- `Linear` – konstante Verzoegerung
- `Comfort` – sanfte S-Kurve
- `AggressiveBrake` – fruehes, kraeftiges Bremsen
- `LateBrake` – spaetes Bremsen mit langer Haltephase
- `BalancedControlPoint` – symmetrischer Mittelpunkt

### Beispiel: Preset direkt anwenden

```csharp
var baseRequest = new DrivingTrajectoryRequest(
    CurrentSpeedKmhPrototype: 90,
    TargetSpeedKmhPrototype: 0,
    DistanceCmModel: 200,
    Scale: 87,
    VMaxKmhPrototype: 120);

var request = TrajectoryPresetFactory.Apply(baseRequest, TrajectoryPreset.LateBrake);
ISpeedTrajectory trajectory = new ParametricTrajectory(request);
```

Getrennte Presets fuer Beschleunigung und Bremsung setzen:

```csharp
var driving = new TrainDriving(train)
{
    AccelerationPreset = AccelerationTrajectoryPreset.Comfort,
    BrakingPreset      = BrakingTrajectoryPreset.AggressiveBrake
};
// Beschleunigung -> Comfort-Profil
await driving.DriveAsync(currentSpeed: 0, targetSpeed: 80, distance: 150);
// Bremsung       -> AggressiveBrake-Profil
await driving.DriveAsync(currentSpeed: 80, targetSpeed: 0, distance: 200);
```

Presets dynamisch zur Laufzeit wechseln:

```csharp
driving.BrakingPreset = BrakingTrajectoryPreset.LateBrake;
await driving.DriveAsync(currentSpeed: 80, targetSpeed: 0, distance: 200);
```

### Beispiel: Preset ueber TrainDriving-Facade

```csharp
var driving = new TrainDriving(train);

var request = new DrivingTrajectoryRequest(
    CurrentSpeedKmhPrototype: 80,
    TargetSpeedKmhPrototype: 0,
    DistanceCmModel: 200,
    Scale: 87,
    VMaxKmhPrototype: train.VMax);

var trajectory = driving.CreatePresetTrajectory(request, TrajectoryPreset.AggressiveBrake);
var executor = driving.CreateExecutor(train, trajectory);

await executor.ExecuteAtDistanceAsync(100);
```

## Executor (Integration mit Train)

```csharp
var executor = driving.CreateExecutor(train, trajectory);
await executor.ExecuteAtDistanceAsync(traveledCm);
```

## Hinweise

- Distanz ist Modellstrecke in `cm`
- Geschwindigkeit ist Vorbild in `km/h`
- Massstab (`Scale`) wird intern auf Vorbildstrecke umgerechnet
