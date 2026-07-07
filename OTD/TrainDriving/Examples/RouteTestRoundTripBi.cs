// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using OTD.Common;
using OTD.HardwareControl;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Services;
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
        Logging.EnableConsole = true;
        Logging.EnableDebugFor<LocoDecoder>(extended: false);
        Logging.EnableDebugFor<TrainDriving>(extended: true);
        Logging.EnableDebugFor<RouteController>(extended: true);
        Logging.EnableDebugFor<RouteLegResolver>(extended: true);
        Logging.EnableDebugFor<RouteTableService>(extended: true);

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
            var layoutService = new XmlRailwayLayoutService(
                topologyFilePath: XmlRailwayLayoutService.GetDefaultTopologyFilePath());
            var routeDefinitions = new XmlRouteDefinitionService(layoutService);
            using var controller = new RouteController(train, routeDefinitions, layoutService, initialHold: true);

            controller.AccelerationMs2 = 3;
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

            // Fahrstrasse (Weiche) S_C2 -> S_E104 stellen.
            Console.WriteLine("[Fahrstrasse] S_C2 -> S_E104: W5/6=straight");
            await _threeWayW5W6.SetStateAsync("straight", cts.Token);

            // Route zu RouteTable hinzufügen.
            controller.AddRoute(new RouteLeg(
                FromWaypointId: "S_C2",
                ToWaypointId: "S_E104",
                DriveProfile: new RouteDriveProfile(
                    AccelerationPreset: AccelerationTrajectoryPreset.Linear,
                    BrakingPreset: BrakingTrajectoryPreset.Linear))
            {
                AccelerationStartPolicy = AccelerationStartPolicy.AfterTrainClearsWaypoint
            });

            Console.WriteLine("[Test] Route S_C2 -> S_E104 bereit. Zur Abfahrt beliebige Taste drücken.");
            Console.ReadKey(true);
            controller.ReleaseGo();

            // Fahrstrasse S_E104 -> S_I41 (Weichen) stellen.
            Console.WriteLine("[Fahrstrasse] S_E104 -> S_I41: W101=straight, W102=diverging, W103=diverging");
            await _turnoutW101.SetStateAsync("straight", cts.Token);
            await _turnoutW102.SetStateAsync("diverging", cts.Token);
            await _turnoutW103.SetStateAsync("diverging", cts.Token);

            // Route S_E104 -> S_I41: Route zu RouteTable hinzufügen.
            controller.AddRoute(new RouteLeg(
                FromWaypointId: "S_E104",
                ToWaypointId: "S_I41",
                DriveProfile: new RouteDriveProfile(
                    AccelerationPreset: AccelerationTrajectoryPreset.Linear,
                    BrakingPreset: BrakingTrajectoryPreset.Linear))
            {
                AccelerationStartPolicy = AccelerationStartPolicy.AfterTrainClearsWaypoint
            });

            // Fahrstrasse S_I41 -> S_A13 stellen.
            Console.WriteLine("[Fahrstrasse] S_I41 -> S_A13: W104=diverging");
            await _turnoutW104.SetStateAsync("diverging", cts.Token);

            controller.AddRoute(
                new RouteLeg(
                    FromWaypointId: "S_I41",
                    ToWaypointId: "S_A13",
                    DriveProfile: new RouteDriveProfile(
                        AccelerationPreset: AccelerationTrajectoryPreset.Linear,
                        BrakingPreset: BrakingTrajectoryPreset.Linear))
                {
                    AccelerationStartPolicy = AccelerationStartPolicy.AfterTrainClearsWaypoint
                }
            );

//            Console.WriteLine("[Test] Einfahrt S_A13 -> S_C2 mit beliebiger Taste.");
//            Console.ReadKey(true);

            // Fahrstrasse S_A13 -> S_C2 stellen und Route erneut erweitern.
            Console.WriteLine("[Fahrweg] S_A13 -> S_C2 -> W5/6=diverging");
            await _turnoutW1.SetStateAsync("diverging", cts.Token);
            await _turnoutW2.SetStateAsync("crossing-straight", cts.Token);

            // Route S_A13 -> S_C2 zu RouteTable hinzufügen.
            controller.AddRoute(new RouteLeg(
                    FromWaypointId: "S_A13",
                    ToWaypointId: "S_C2",
                    MaxSpeedKmh: 40,
                    DriveProfile: new RouteDriveProfile(
                        AccelerationPreset: AccelerationTrajectoryPreset.Linear,
                        BrakingPreset: BrakingTrajectoryPreset.Linear),
                    StopPointToTargetCm: 25)

                {
                    AccelerationStartPolicy = AccelerationStartPolicy.AfterTrainClearsWaypoint
                }
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