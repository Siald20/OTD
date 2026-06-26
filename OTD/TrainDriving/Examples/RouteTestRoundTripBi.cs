// // SPDX-License-Identifier: GPL-3.0-or-later
// //
// // OpenTrainDrive - DecoderControl
// // Copyright (C) 2026
// //
// // Authors:
// // - Hansueli Alder <name@example.com>
// //
// // Dieses Programm ist freie Software: Sie koennen es unter den Bedingungen
// // der GNU General Public License, wie von der Free Software Foundation,
// // entweder Version 3 der Lizenz oder (nach Ihrer Wahl) jeder spaeteren
// // veroeffentlichten Version, weiterverbreiten und/oder modifizieren.
// //
// // Dieses Programm wird in der Hoffnung bereitgestellt, dass es nuetzlich sein wird,
// // jedoch OHNE JEDE GEWAEHRLEISTUNG; sogar ohne die implizite Gewaehrleistung der
// // MARKTFAEHIGKEIT oder EIGNUNG FUER EINEN BESTIMMTEN ZWECK.
// // Siehe die GNU General Public License fuer weitere Details.
// //
// // Sie sollten eine Kopie der GNU General Public License zusammen mit diesem
// // Programm erhalten haben. Falls nicht, siehe <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OTD.HardwareControl;
using OTD.TrainDriving.Presets;
using OTD.TrainDriving.RouteModel;

namespace OTD.TrainDriving.Examples;

public class RouteTestRoundTripBi
{
    private sealed record SensorSpec(int SensorId, int OffsetCm);

    private static Accessory? _turnoutW1;
    private static Accessory? _turnoutW2;
    private static Accessory? _threeWayW5W6;
    private static Accessory? _turnoutW101;
    private static Accessory? _turnoutW102;
    private static Accessory? _turnoutW103;
    private static Accessory? _turnoutW104;
    private static Train? _trainBr193;

    public static void Run(CommandStation commandStation, Feedback feedbackModule)
    {
        ArgumentNullException.ThrowIfNull(commandStation);
        ArgumentNullException.ThrowIfNull(feedbackModule);
        RunAsync(commandStation, feedbackModule).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(CommandStation commandStation, Feedback feedbackModule)
    {
        using var cts = new CancellationTokenSource();
        EventHandler<SensorStateChangedEventArgs>? feedbackHandler = null;

        try
        {
            // Hardwareobjekte gemäss accessories.xml aufbauen.
            _turnoutW1 = new Accessory(Guid.Parse("3f8a1b2c-4d5e-4f7a-8b9c-0d1e2f3a4b5c"), commandStation);
            _turnoutW2 = new Accessory(Guid.Parse("4a9b2c3d-5e6f-4a7b-9c0d-1e2f3a4b5c6d"), commandStation);
            _threeWayW5W6 = new Accessory(Guid.Parse("5b0c3d4e-6f7a-4b8c-9d0e-2f3a4b5c6d7e"), commandStation);
            _turnoutW101 = new Accessory(Guid.Parse("cb5275e6-8b00-4d7d-b090-25a6db69624e"), commandStation);
            _turnoutW102 = new Accessory(Guid.Parse("2b3c4d5e-6f70-4812-9304-0b1c2d3e4f5a"), commandStation);
            _turnoutW103 = new Accessory(Guid.Parse("3c4d5e6f-7081-4923-a405-1c2d3e4f5a6b"), commandStation);
            _turnoutW104 = new Accessory(Guid.Parse("4d5e6f70-8192-4a34-b506-2d3e4f5a6b7c"), commandStation);

            // Testzug BR193 initialisieren.
            _trainBr193 = new Train(Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6e"), commandStation);
            _trainBr193.OperatingMode = TrainOperatingMode.Travelling;
            _trainBr193.TrainDirection = TrainDirection.A;

            // Fahrdynamik für die Testfahrt.
            var driving = new TrainDriving(_trainBr193)
            {
                AccelerationPreset = AccelerationTrajectoryPreset.Linear,
                AccelerationMs2 = 0.55,
                BrakingPreset = BrakingTrajectoryPreset.Linear
            };

            // RouteTable wird im Test dynamisch erweitert.
            var routeEntries = new List<RouteEntry>();
            var sensorMarkers = new List<SensorMarker>();
            RouteRuntime? runtime = null;

            var nextRouteId = 1;
            double routePositionCm = 0;
            var currentSpeedKmh = 0;
            var runtimeStateLock = new object();
            var configuredSensorIds = new HashSet<int>();
            // Sensoren nur beim ersten Active-Event auswerten.
            var triggeredSensors = new HashSet<int>();

            RouteEntry AddRoute(string fromWaypointId, string toWaypointId, int distanceCm, int vmaxKmh, params SensorSpec[] sensors)
            {
                // Jede Route startet mit einer expliziten Fahrerlaubnis (Variante 2).
                var routeId = nextRouteId++;
                var entry = new RouteEntry(
                    Id: routeId,
                    FromWaypointId: fromWaypointId,
                    ToWaypointId: toWaypointId,
                    DistanceCm: distanceCm,
                    StartRoutePermission: RoutePermission.Proceed(vmaxKmh, aspect: $"Vmax {vmaxKmh}"),
                    DriveProfile: new RouteDriveProfile(
                        AccelerationPreset: AccelerationTrajectoryPreset.EarlyAcceleration,
                        BrakingPreset: BrakingTrajectoryPreset.LateBrake));

                routeEntries.Add(entry);
                // Sensoren mit echter Feedback-Sensornummer als Marker innerhalb der Route hinterlegen.
                foreach (var sensor in sensors)
                {
                    configuredSensorIds.Add(sensor.SensorId);
                    sensorMarkers.Add(new SensorMarker(
                        RouteId: routeId,
                        OffsetCm: sensor.OffsetCm,
                        SensorId: sensor.SensorId));
                }

                Console.WriteLine($"[Route] + {fromWaypointId} -> {toWaypointId}, Distanz={distanceCm}cm, Vmax={vmaxKmh} km/h");
                return entry;
            }

            void AddStopPlaceholder(string waypointId)
            {
                // 0-cm-Halt-Route als Platzhalter, solange kein Folgeverlauf freigegeben ist.
                var routeId = nextRouteId++;
                routeEntries.Add(new RouteEntry(
                    Id: routeId,
                    FromWaypointId: waypointId,
                    ToWaypointId: waypointId,
                    DistanceCm: 0,
                    StartRoutePermission: RoutePermission.Stop(aspect: "Halt")));
                Console.WriteLine($"[Route] + Platzhalter {waypointId} -> {waypointId}, Distanz=0cm, Halt");
            }

            void RebuildRuntime()
            {
                // Runtime nach jeder Routenänderung mit aktueller Position neu aufbauen.
                var table = new RouteTableBuilder()
                    .AddRoute(routeEntries)
                    .AddSensorMarkers(sensorMarkers)
                    .Build();

                RouteRuntimeTickResult tick;
                lock (runtimeStateLock)
                {
                    var tracker = new PositionTracker(table, initialPositionCm: routePositionCm);
                    runtime = new RouteRuntime(table, tracker);
                    tick = runtime.ApplyStep(deltaCm: 0, trajectorySpeedKmh: currentSpeedKmh);
                }

                Console.WriteLine(
                    $"[RouteRuntime] pos={tick.EstimatedPositionCm:F1}cm, active={tick.ActiveCycle?.FromWaypoint.WaypointId}->{tick.ActiveCycle?.ToWaypoint.WaypointId}, " +
                    $"allowed={tick.ActiveCycle?.AllowedSpeedKmh}");
            }

            feedbackHandler = (_, args) =>
            {
                if (args.State != RailSensorState.Active)
                    return;

                if (!configuredSensorIds.Contains(args.SensorNumber))
                    return;

                RouteRuntimeTickResult tick;
                lock (runtimeStateLock)
                {
                    if (runtime is null)
                        return;

                    if (!triggeredSensors.Add(args.SensorNumber))
                        return;

                    tick = runtime.ApplyStep(
                        deltaCm: 0,
                        trajectorySpeedKmh: currentSpeedKmh,
                        activatedSensorId: args.SensorNumber);

                    routePositionCm = tick.EstimatedPositionCm;
                }

                Console.WriteLine(
                    $"[Feedback->RouteRuntime] Sensor={args.SensorNumber}, recal={tick.PositionRecalibrated}, " +
                    $"calcPos={tick.EstimatedPositionBeforeRecalibrationCm:F1}cm, correctedPos={tick.EstimatedPositionCm:F1}cm, " +
                    $"err={tick.CorrectionErrorCm?.ToString("F1") ?? "-"}cm");
            };
            feedbackModule.SensorStateChanged += feedbackHandler;

            // Führt einen Streckenabschnitt physisch aus und synchronisiert danach den simulierten Routenfortschritt.
            async Task DriveDistanceAsync(int targetSpeedKmh, int distanceCm, RouteRuntime? runtimeSnapshot, CancellationToken token)
            {
                if (distanceCm <= 0)
                    return;

                var accumulatedDistanceCm = 0;
                Action<TrainDrivingProgressTick> progressHandler = tick =>
                {
                    lock (runtimeStateLock)
                    {
                        currentSpeedKmh = tick.CommandedSpeedKmhPrototype;
                        accumulatedDistanceCm += tick.DeltaCmModel;

                        if (runtimeSnapshot is null)
                        {
                            routePositionCm += tick.DeltaCmModel;
                            return;
                        }

                        var runtimeTick = runtimeSnapshot.ApplyStep(
                            deltaCm: tick.DeltaCmModel,
                            trajectorySpeedKmh: tick.CommandedSpeedKmhPrototype);
                        routePositionCm = runtimeTick.EstimatedPositionCm;
                    }
                };

                // Physische Fahrt ausführen.
                driving.ProgressTick += progressHandler;
                try
                {
                    var currentTrainSpeed = _trainBr193?.SpeedV ?? 0;
                    if (targetSpeedKmh > currentTrainSpeed)
                    {
                        await driving.AccelerateAsync(targetSpeedKmh, token);

                        var remainingDistance = Math.Max(0, distanceCm - accumulatedDistanceCm);
                        if (remainingDistance > 0)
                            await driving.BrakeAsync(targetSpeedKmh, remainingDistance, token);
                    }
                    else
                    {
                        await driving.BrakeAsync(targetSpeedKmh, distanceCm, token);
                    }
                }
                finally
                {
                    driving.ProgressTick -= progressHandler;
                }

                lock (runtimeStateLock)
                {
                    currentSpeedKmh = targetSpeedKmh;

                    if (runtimeSnapshot is not null)
                    {
                        var tick = runtimeSnapshot.ApplyStep(deltaCm: 0, trajectorySpeedKmh: currentSpeedKmh);
                        routePositionCm = tick.EstimatedPositionCm;
                        Console.WriteLine(
                            $"[RouteRuntime] nach Fahrt: pos={tick.EstimatedPositionCm:F1}cm, cmd={tick.EffectiveSpeedKmh} km/h, " +
                            $"permission={tick.PermissionState.SourceWaypointId}/{tick.PermissionState.MaxSpeedKmh}");
                    }
                }
            }

            // 1) Startzustand: B2 auf Halt.
            Console.WriteLine("[Test2] Start: BR193 steht bei B2 (Halt).");
            AddStopPlaceholder("B2");
            RebuildRuntime();

            // 2) 5 Sekunden warten.
            Console.WriteLine("[Test2] 5 warten...");
            await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);

            // 3) Fahrweg B2 -> K102 stellen.
            Console.WriteLine("[Fahrweg] B2 -> K102: W2=straight-crossing, W1=diverging");
            await _turnoutW2.SetStateAsync("crossing-straight", cts.Token);
            await _turnoutW1.SetStateAsync("diverging", cts.Token);

            // 4) Route B2 -> K102 ergänzen (223cm, 40 km/h).
            var b2ToK102 = AddRoute("B2", "K102", 223, 40,
                new SensorSpec(28, 3),
                new SensorSpec(31, 43),
                new SensorSpec(30, 73));
            RebuildRuntime();

            // 5) Fahrweg Richtung H41 vorbereiten.
            Console.WriteLine("[Fahrweg] Richtung H41: W104=diverging");
            await _turnoutW104.SetStateAsync("diverging", cts.Token);

            // 6) 10 Sekunden warten, dann bei B2 abfahren.
            Console.WriteLine("[Test2] 10s warten, dann Abfahrt bei B2...");
            await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
            await DriveDistanceAsync(targetSpeedKmh: 40, distanceCm: b2ToK102.DistanceCm, runtime, cts.Token);

            // 7) Route K102 -> H41 ergänzen (163cm, 60 km/h).
            var k102ToH41 = AddRoute("K102", "H41", 163, 60,
                new SensorSpec(148, 10),
                new SensorSpec(147, 53));
            RebuildRuntime();

            Console.WriteLine("[Test2] Fahrt K102 -> H41");
            await DriveDistanceAsync(targetSpeedKmh: 60, distanceCm: k102ToH41.DistanceCm, runtime, cts.Token);

            // 8) 10 Sekunden warten (Simulation Blockbelegung voraus).
            Console.WriteLine("[Test2] 10s warten (Simulation: vorausliegende Route noch besetzt)...");
            await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);

            // 9) Fahrweg H41 -> B12 stellen.
            Console.WriteLine("[Fahrweg] H41 -> B12: W103=diverging, W102=diverging, W101=straight");
            await _turnoutW103.SetStateAsync("diverging", cts.Token);
            await _turnoutW102.SetStateAsync("diverging", cts.Token);
            await _turnoutW101.SetStateAsync("straight", cts.Token);

            // 10) Route H41 -> B12 ergänzen + Halt-Platzhalter hinter B12.
            var h41ToB12 = AddRoute("H41", "B12", 290, 60,
                new SensorSpec(146, 10),
                new SensorSpec(151, 39),
                new SensorSpec(56, 74));
            AddStopPlaceholder("B12");
            RebuildRuntime();

            Console.WriteLine("[Test2] Fahrt H41 -> B12 mit Halt am Zielsignal");
            // Zweistufig: erst Strecke mit Fahrt, dann Ausrollen auf Halt.
            await DriveDistanceAsync(targetSpeedKmh: 60, distanceCm: 220, runtime, cts.Token);
            await DriveDistanceAsync(targetSpeedKmh: 0, distanceCm: h41ToB12.DistanceCm - 220, runtime, cts.Token);

            // 11) 20 Sekunden warten (Simulation Blockbelegung voraus).
            Console.WriteLine("[Test2] 20s warten (Simulation: vorausliegende Route noch belegt)...");
            await Task.Delay(TimeSpan.FromSeconds(20), cts.Token);

            // 12) Fahrweg B12 -> B2 stellen.
            Console.WriteLine("[Fahrweg] B12 -> B2: W5/6=straight");
            await _threeWayW5W6.SetStateAsync("straight", cts.Token);

            // Platzhalter entfernen und echte Route B12 -> B2 setzen.
            routeEntries.RemoveAt(routeEntries.Count - 1);
            var b12ToB2 = AddRoute("B12", "B2", 243, 50,
                new SensorSpec(54, 63),
                new SensorSpec(52, 104));
            RebuildRuntime();

            Console.WriteLine("[Test2] Abfahrt bei B12");
            // Auch ohne erneuten Halt-Platzhalter soll der Zug vor B2 stoppen.
            await DriveDistanceAsync(targetSpeedKmh: 50, distanceCm: 180, runtime, cts.Token);
            await DriveDistanceAsync(targetSpeedKmh: 0, distanceCm: b12ToB2.DistanceCm - 180, runtime, cts.Token);

            Console.WriteLine("[Test2] BR193 sollte vor B2 stehen (ohne leere Anschlussroute am Ziel). ");
        }
        finally
        {
            if (feedbackHandler is not null)
                feedbackModule.SensorStateChanged -= feedbackHandler;

            // Hardwareobjekte sauber freigeben.
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