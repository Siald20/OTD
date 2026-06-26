# Rollback 1

Dieser Snapshot enthält den Stand **vor der Straffung von `TrajectoryDelayConfig` in `TrainDriving`**.

## Enthaltene Dateien

- `TrainDriving/TrainDriving.cs`
- `TrainDriving/DecoderSpeedResponseModel.cs`
- `TrainDriving/Test1.cs`

## Wiederherstellung

```bash
cp /home/hua/RiderProjects/OTD/scripts/rollback/rollback1/TrainDriving/TrainDriving.cs /home/hua/RiderProjects/OTD/OTD/TrainDriving/TrainDriving.cs
cp /home/hua/RiderProjects/OTD/scripts/rollback/rollback1/TrainDriving/DecoderSpeedResponseModel.cs /home/hua/RiderProjects/OTD/OTD/TrainDriving/DecoderSpeedResponseModel.cs
cp /home/hua/RiderProjects/OTD/scripts/rollback/rollback1/TrainDriving/Test1.cs /home/hua/RiderProjects/OTD/OTD/TrainDriving/Examples/Test1.cs
```

