// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using OTD.Common;
using OTD.HardwareControl;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.Trajectory;

namespace OTD.TrainDriving.Examples;

/// <summary>
/// Rundfahrt mit RouteController auf Basis der neuen RouteControl-API.
/// </summary>
public static class RouteTestRoundTripBi
{
    private sealed class SensorFeedbackForwarder(RouteController controller)
    {
        public void OnSensorStateChanged(object? _, SensorStateChangedEventArgs args)
        {
            if (args.State != RailSensorState.Active)
                return;

            Console.WriteLine($"[Sensor] {args.SensorNumber} aktiv");
            controller.OnSensorActivated(args.SensorNumber);
        }
    }

    private static Accessory? _turnoutW1;
    private static Accessory? _turnoutW2;
    private static Accessory? _threeWayW5W6;
    private static Accessory? _turnoutW101;
    private static Accessory? _turnoutW102;
    private static Accessory? _turnoutW103;
    private static Accessory? _turnoutW104;

    public static void Run(CommandStation commandStation, Feedback feedbackModule)
    {
        ArgumentNullException.ThrowIfNull(commandStation);
        ArgumentNullException.ThrowIfNull(feedbackModule);
        Logging.EnableDebugFor<TrainDriving>(extended: true);
        RunAsync(commandStation, feedbackModule).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(CommandStation commandStation, Feedback feedbackModule)
    {
        using var cts = new CancellationTokenSource();
        EventHandler<SensorStateChangedEventArgs>? feedbackHandler = null;

        try
        {
            // Hardwareobjekte gemaess accessories.xml aufbauen.
            _turnoutW1 = new Accessory(Guid.Parse("3f8a1b2c-4d5e-4f7a-8b9c-0d1e2f3a4b5c"), commandStation);
            _turnoutW2 = new Accessory(Guid.Parse("4a9b2c3d-5e6f-4a7b-9c0d-1e2f3a4b5c6d"), commandStation);
            _threeWayW5W6 = new Accessory(Guid.Parse("5b0c3d4e-6f7a-4b8c-9d0e-2f3a4b5c6d7e"), commandStation);
            _turnoutW101 = new Accessory(Guid.Parse("cb5275e6-8b00-4d7d-b090-25a6db69624e"), commandStation);
            _turnoutW102 = new Accessory(Guid.Parse("2b3c4d5e-6f70-4812-9304-0b1c2d3e4f5a"), commandStation);
            _turnoutW103 = new Accessory(Guid.Parse("3c4d5e6f-7081-4923-a405-1c2d3e4f5a6b"), commandStation);
            _turnoutW104 = new Accessory(Guid.Parse("4d5e6f70-8192-4a34-b506-2d3e4f5a6b7c"), commandStation);

            var train = new Train(Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6e"), commandStation);
            using var controller = new RouteController(train, initialHold: true);

            controller.AccelerationMs2 = 0.55;
            controller.UseAdaptiveSpeedStepInterval = true;
            controller.MinSpeedStepInterval = TimeSpan.FromMilliseconds(250);
            controller.MaxSpeedStepInterval = TimeSpan.FromMilliseconds(1000);
            controller.BoundTrain.OperatingMode = TrainOperatingMode.Travelling;
            controller.BoundTrain.TrainDirection = TrainDirection.A;

            // Sensor-Feedback an den Controller weiterreichen.
            var sensorFeedbackForwarder = new SensorFeedbackForwarder(controller);
            feedbackHandler = sensorFeedbackForwarder.OnSensorStateChanged;
            feedbackModule.SensorStateChanged += feedbackHandler;

            Console.WriteLine("[Test] Start: Zug steht in Bitschikon Gleis 2.");
            
            var driveTask = controller.Run(cts.Token);
            
            // Fahrstrasse (Weichen) B2 -> K102 stellen.
            Console.WriteLine("[Fahrstrasse] B2 -> K102: W2=straight-crossing, W1=diverging");
            await _turnoutW2.SetStateAsync("crossing-straight", cts.Token);
            await _turnoutW1.SetStateAsync("diverging", cts.Token);

            // Route B2 -> K102: Weichenbereich (40 km/h) zu RouteTable hinzufügen.
            controller.AddRoute(new RouteLeg(
                FromWaypointId: "B2",
                ToWaypointId: "W1",
                DistanceCm: 73,
                MaxSpeedKmh: 40,
                DriveProfile: new RouteDriveProfile(
                    AccelerationPreset: AccelerationTrajectoryPreset.Linear,
                    BrakingPreset: BrakingTrajectoryPreset.Linear),
                SensorMarkers:
                [
                    new SensorMarker(SensorId: 28, OffsetCm: 3),
                    new SensorMarker(SensorId: 31, OffsetCm: 43),
                ]));          
            
            // Route B2 -> K102: Streckenblock (60 km/h) zu RouteTable hinzufügen.
            controller.AddRoute(new RouteLeg(
                FromWaypointId: "W1",
                ToWaypointId: "K102",
                DistanceCm: 150,
                MaxSpeedKmh: 60,
                DriveProfile: new RouteDriveProfile(
                    AccelerationPreset: AccelerationTrajectoryPreset.Linear,
                    BrakingPreset: BrakingTrajectoryPreset.Linear),
                SensorMarkers:
                [
                    new SensorMarker(SensorId: 30, OffsetCm: 73)
                ]));
            
            // Fahrstrasse (Weiche) K102 -> H41 stellen.
            Console.WriteLine("[Fahrstrasse] K102 -> H41: W104=diverging");
            await _turnoutW104.SetStateAsync("diverging", cts.Token);            
            
            controller.AddRoute(
                new RouteLeg(
                    FromWaypointId: "K102",
                    ToWaypointId: "H41",
                    DistanceCm: 163,
                    MaxSpeedKmh: 80,
                    DriveProfile: new RouteDriveProfile(
                        AccelerationPreset: AccelerationTrajectoryPreset.Linear,
                        BrakingPreset: BrakingTrajectoryPreset.Linear),
                    SensorMarkers:
                    [
                        new SensorMarker(SensorId: 148, OffsetCm: 10),
                        new SensorMarker(SensorId: 147, OffsetCm: 53)
                    ])
                );

            Console.WriteLine("[Test] Route B2 -> H41 bereit. Zur Abfahrt beliebige Taste drücken.");
            Console.ReadKey(true);
            controller.ReleaseGo();

            // Fahrstrasse H41 -> B12 (Weichen) stellen.
            Console.WriteLine("[Fahrweg] H41 -> B12: W103=diverging, W102=diverging, W101=straight");
            await _turnoutW103.SetStateAsync("diverging", cts.Token);
            await _turnoutW102.SetStateAsync("diverging", cts.Token);
            await _turnoutW101.SetStateAsync("straight", cts.Token);

            // Route H41 -> B12 zu RouteTable hinzufügen (fährt autonom in B12 ein).
            controller.AddRoute(new RouteLeg(
                FromWaypointId: "H41",
                ToWaypointId: "B12",
                DistanceCm: 290,
                MaxSpeedKmh: 60,
                DriveProfile: new RouteDriveProfile(
                    AccelerationPreset: AccelerationTrajectoryPreset.Linear,
                    BrakingPreset: BrakingTrajectoryPreset.Linear),
                SensorMarkers:
                [
                    new SensorMarker(SensorId: 146, OffsetCm: 10),
                    new SensorMarker(SensorId: 151, OffsetCm: 45),
                    new SensorMarker(SensorId: 150, OffsetCm: 78),
                    new SensorMarker(SensorId: 56, OffsetCm: 139)
                ])
                    );

            Console.WriteLine("[Test] Weiterfahrt nach B2 mit beliebiger Taste.");
            Console.ReadKey(true);

            // Fahrstrasse B12 -> B2 stellen und Route erneut erweitern.
            Console.WriteLine("[Fahrweg] B12 -> B2: W5/6=straight");
            await _threeWayW5W6.SetStateAsync("straight", cts.Token);

            // Route B12 -> B2 zu RouteTable hinzufügen (fährt autonom bis B2).
            controller.AddRoute(
                new RouteLeg(
                    FromWaypointId: "B12",
                    ToWaypointId: "B2",
                    DistanceCm: 246,
                    MaxSpeedKmh: 60,
                    DriveProfile: new RouteDriveProfile(
                        AccelerationPreset: AccelerationTrajectoryPreset.Linear,
                        BrakingPreset: BrakingTrajectoryPreset.Linear),
                   StopPoint: new StopPoint(OffsetCm: 220, StopReason: "Zielhalt B2"),
                    SensorMarkers:
                    [
                        new SensorMarker(SensorId: 55, OffsetCm: 55),
                        new SensorMarker(SensorId: 54, OffsetCm: 66),
                        new SensorMarker(SensorId: 52, OffsetCm: 106)
                    ])
                );

            Console.WriteLine("[Test] Demo laeuft. Mit beliebiger Taste beenden.");
            Console.ReadKey(true);

            await cts.CancelAsync();
            try
            {
                await driveTask;
            }
            catch (OperationCanceledException)
            {
                // erwartet
            }
        }
        finally
        {
            if (feedbackHandler is not null)
                feedbackModule.SensorStateChanged -= feedbackHandler;

            DisposeAccessory(ref _turnoutW1);
            DisposeAccessory(ref _turnoutW2);
            DisposeAccessory(ref _threeWayW5W6);
            DisposeAccessory(ref _turnoutW101);
            DisposeAccessory(ref _turnoutW102);
            DisposeAccessory(ref _turnoutW103);
            DisposeAccessory(ref _turnoutW104);
        }
    }

    private static void DisposeAccessory(ref Accessory? accessory)
    {
        accessory?.Dispose();
        accessory = null;
    }
}
