# Rollback 2

Dieser Snapshot enthaelt den Stand **vor der Erweiterung um `CruiseSpeedKmhPrototype` und das 3-Phasen-Fahrprofil (Beschleunigen/Halten/Bremsen)**.

## Enthaltene Dateien

- `TrainDriving/TrainDriving.cs`
- `TrainDriving/DrivingTrajectoryRequest.cs`
- `TrainDriving/README.md`

## Wiederherstellung

```bash
cp /home/hua/RiderProjects/OTD/scripts/rollback/rollback2/TrainDriving/TrainDriving.cs /home/hua/RiderProjects/OTD/OTD/TrainDriving/TrainDriving.cs
cp /home/hua/RiderProjects/OTD/scripts/rollback/rollback2/TrainDriving/DrivingTrajectoryRequest.cs /home/hua/RiderProjects/OTD/OTD/TrainDriving/Trajectory/DrivingTrajectoryRequest.cs
cp /home/hua/RiderProjects/OTD/scripts/rollback/rollback2/TrainDriving/README.md /home/hua/RiderProjects/OTD/OTD/TrainDriving/README.md
```

