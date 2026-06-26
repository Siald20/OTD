// // SPDX-License-Identifier: GPL-3.0-or-later
// //
// // OpenTrainDrive - DecoderControl
// // Copyright (C) 2026
// //
// // Authors:
// // - Hansueli Alder <name@example.com>
// //
// // Dieses Programm ist freie Software: Sie können es unter den Bedingungen
// // der GNU General Public License, wie von der Free Software Foundation,
// // entweder Version 3 der Lizenz oder (nach Ihrer Wahl) jeder späteren
// // veröffentlichten Version, weiterverbreiten und/oder modifizieren.
// //
// // Dieses Programm wird in der Hoffnung bereitgestellt, dass es nützlich sein wird,
// // jedoch OHNE JEDE GEWÄHRLEISTUNG; sogar ohne die implizite Gewährleistung der
// // MARKTFÄHIGKEIT oder EIGNUNG FÜR EINEN BESTIMMTEN ZWECK.
// // Siehe die GNU General Public License für weitere Details.
// //
// // Sie sollten eine Kopie der GNU General Public License zusammen mit diesem
// // Programm erhalten haben. Falls nicht, siehe <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OTD.Common;
using OTD.HardwareControl;
using OTD.TrainDriving.Presets;
using OTD.TrainDriving.RouteModel;

namespace OTD.TrainDriving.Examples;

public class TrayectoryTestSingleSensor
{
    private static void LogTimed(string channel, string message)
    {
        if (string.Equals(channel, "TrainDriving", StringComparison.OrdinalIgnoreCase))
        {
            Logging.Info(LogCategory.TrainDriving, message);
            return;
        }

        Console.WriteLine($"[{channel}] {DateTimeOffset.Now:HH:mm:ss.fff} {message}");
    }

    // verwendete Weichen
    private static Accessory? _turnoutW1; // Bi3 -> Bi13
    private static Accessory? _threeWayW5W6; // Bi3 -> Bi91
    private static Accessory? _turnoutW101; // Abzweigung -> Bi91
    private static Accessory? _turnoutW102; // Gleiswechsel
    private static Accessory? _turnoutW103; // Gleiswechsel
    private static Accessory? _turnoutW104; // Bi41 - Bi13 (Abzweigung Tiefengrund)

    // verwendeter Rückmelder
    private static Feedback? _feedbackModule;

    // verwendete Züge
    private static Train? _testTrain;

    private static RouteRuntime? _activeRouteRuntime;

    // Prototypische Zuordnung Hardware-Sensornummer -> RouteModel SensorId.
    private static readonly Dictionary<int, int> RouteSensorIdMap = new()
    {
        { 56, 4 }
    };

    private static readonly SemaphoreSlim EmergencyStopGate = new(1, 1);

    public static void Run(CommandStation commandStation, Feedback feedbackModule)
    {
//        DecoderDelayExampleRunner.Run();
//        return;
        ArgumentNullException.ThrowIfNull(commandStation);
        ArgumentNullException.ThrowIfNull(feedbackModule);

        RunAsync(commandStation, feedbackModule).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(CommandStation commandStation, Feedback feedbackModule)
    {
        using var emergencyHotkeyCts = new CancellationTokenSource();
        var emergencyHotkeyTask = StartEmergencyStopHotkeyListenerAsync(emergencyHotkeyCts.Token);
        EventHandler<SensorStateChangedEventArgs>? feedbackEventLogger = null;
        _activeRouteRuntime = null;
        Logging.EnableDebugForCategory(LogCategory.TrainDriving);

        try
        {
            _feedbackModule = feedbackModule;
            //
            // feedbackEventLogger = (_, args) =>
            // {
            //     Console.WriteLine($"[Feedback] {DateTime.Now:HH:mm:ss.fff} Sensor {args.SensorNumber} => {args.State}");
            //
            //     // Sensor-Trigger als Positionsabgleich in RouteRuntime (nur bei aktivem Kontakt).
            //     if (args.State != RailSensorState.Active || _activeRouteRuntime is null)
            //         return;
            //
            //     if (!RouteSensorIdMap.TryGetValue(args.SensorNumber, out var routeSensorId))
            //         return;
            //
            //     var tick = _activeRouteRuntime.ApplyStep(
            //         deltaCm: 0,
            //         trajectorySpeedKmh: VTest,
            //         activatedSensorId: routeSensorId);
            //
            //     Console.WriteLine(
            //         $"[RouteRuntime] Sensorabgleich {routeSensorId}: " +
            //         $"recal={tick.PositionRecalibrated}, pos={tick.EstimatedPositionCm:F1}cm, err={tick.CorrectionErrorCm:F1}cm");
            // };
            // _feedbackModule.SensorStateChanged += feedbackEventLogger;

            // Weichen initialisieren
            _turnoutW1 = new Accessory(Guid.Parse("3f8a1b2c-4d5e-4f7a-8b9c-0d1e2f3a4b5c"), commandStation) ??
                         throw new InvalidOperationException("Turnout W1 not initialized.");
            _threeWayW5W6 = new Accessory(Guid.Parse("5b0c3d4e-6f7a-4b8c-9d0e-2f3a4b5c6d7e"), commandStation) ??
                            throw new InvalidOperationException("Turnout W5/6 not initialized.");
            _turnoutW101 = new Accessory(Guid.Parse("cb5275e6-8b00-4d7d-b090-25a6db69624e"), commandStation) ??
                           throw new InvalidOperationException("Turnout W101 not initialized.");
            _turnoutW102 = new Accessory(Guid.Parse("2b3c4d5e-6f70-4812-9304-0b1c2d3e4f5a"), commandStation) ??
                           throw new InvalidOperationException("Turnout W102 not initialized.");
            _turnoutW103 = new Accessory(Guid.Parse("3c4d5e6f-7081-4923-a405-1c2d3e4f5a6b"), commandStation) ??
                           throw new InvalidOperationException("Turnout W103 not initialized.");
            _turnoutW104 = new Accessory(Guid.Parse("4d5e6f70-8192-4a34-b506-2d3e4f5a6b7c"), commandStation) ??
                           throw new InvalidOperationException("Turnout 104 not initialized.");

            // Züge initialisieren
//            _testTrain = new Train(Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6c"), commandStation) ??
//                         throw new InvalidOperationException("Train DT612 is not initialized.");
            _testTrain = new Train(Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6e"), commandStation) ??
                          throw new InvalidOperationException("Train BR193 is not initialized.");

            // Weichen stellen (für Rundkurs BI)
            await _turnoutW1.SetStateAsync("straight", emergencyHotkeyCts.Token);
            await _threeWayW5W6.SetStateAsync("left", emergencyHotkeyCts.Token);
            await _turnoutW101.SetStateAsync("straight", emergencyHotkeyCts.Token);
            await _turnoutW102.SetStateAsync("diverging", emergencyHotkeyCts.Token);
            await _turnoutW103.SetStateAsync("diverging", emergencyHotkeyCts.Token);
            await _turnoutW104.SetStateAsync("diverging", emergencyHotkeyCts.Token);

            _testTrain.OperatingMode = TrainOperatingMode.Travelling;
            _testTrain.TrainDirection = TrainDirection.A;

            Console.WriteLine("[Train] OperatingMode=Travelling, TrainDirection=A");

            // Zug abfahren lassen
            const int vTest = 126; // Test-Geschwindigkeit in km/h
            var trainDriving = new TrainDriving(_testTrain)
            {
                AccelerationPreset = AccelerationTrajectoryPreset.Linear,
                AccelerationMs2 = 1.5,
                BrakingPreset = BrakingTrajectoryPreset.Linear,
                UseAdaptiveSpeedStepInterval = true,
                MinSpeedStepInterval = TimeSpan.FromMilliseconds(250),
                MaxSpeedStepInterval = TimeSpan.FromMilliseconds(1000),
                BrakePointCorrectionPercent = -3.5,
                BrakePointCorrectionPercentPerVMax = 0.0,
                SpeedCurveFidelityPercent = 60.0
            };


            using var accelerationCts = CancellationTokenSource.CreateLinkedTokenSource(emergencyHotkeyCts.Token);
            var accelerationTask = trainDriving.AccelerateAsync(
                targetSpeed: vTest,
                cancellationToken: accelerationCts.Token);

            Logging.Info(LogCategory.TrainDriving, "Train departs and accelerates toward target speed.");

            // Warten auf Sensor waehrend die Beschleunigungsrampe noch laeuft.
            LogTimed("Feedback", "Warte auf Sensor 56/147 (Block G91) ...");
            await WaitForSensorStateAsync(_feedbackModule, 56, RailSensorState.Active, emergencyHotkeyCts.Token);

            accelerationCts.Cancel();
            try
            {
                await accelerationTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (accelerationCts.IsCancellationRequested)
            {
                // Erwartet: Die Beschleunigungsrampe wird am Sensorsignal beendet.
            }

            LogTimed("Feedback",
                $"Sensor 56 aktiv -> starte Bremsrampe aus aktueller Geschwindigkeit {_testTrain!.SpeedV} km/h auf 0 km/h ueber 200 cm.");

            // Fallback falls kein gueltiger Routenzyklus verfuegbar ist.
            await trainDriving.BrakeAsync(
                targetSpeed: 0,
                distance: 200, // 153, 110, 200
                cancellationToken: emergencyHotkeyCts.Token);


            // Zug aus Ziel-Block wegfahren
            Console.ReadKey(true);
            await _testTrain.SetSpeedVAsync(20, emergencyHotkeyCts.Token);
            await Task.Delay(TimeSpan.FromSeconds(15), emergencyHotkeyCts.Token);
            await _testTrain.SetSpeedVAsync(0, emergencyHotkeyCts.Token);
        }
        finally
        {
            emergencyHotkeyCts.Cancel();
            try
            {
                await emergencyHotkeyTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Erwartet beim Beenden des Testlaufs.
            }

            if (_feedbackModule is not null && feedbackEventLogger is not null)
            {
                _feedbackModule.SensorStateChanged -= feedbackEventLogger;
            }

            DisposeAccessory(ref _turnoutW1);
            DisposeAccessory(ref _threeWayW5W6);
            _activeRouteRuntime = null;
            _feedbackModule = null;
        }
    }

    private static Task StartEmergencyStopHotkeyListenerAsync(CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            Console.WriteLine("[Safety] SPACE: Nothalt fuer beide Zuege aktiv.");

            // Polling auf Konsoleninput, damit der Testablauf nicht blockiert.
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!Console.KeyAvailable)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var keyInfo = Console.ReadKey(true);
                if (keyInfo.Key != ConsoleKey.Spacebar)
                    continue;

                await EmergencyStopAllTrainsAsync().ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    // Löst eine Notbremsung für alle Züge aus (mit Leertaste)
    private static async Task EmergencyStopAllTrainsAsync()
    {
        // Gate verhindert parallele Mehrfachauslösungen bei schneller Tasteneingabe.
        await EmergencyStopGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Console.WriteLine("[Safety] SPACE erkannt -> Nothalt fuer VT612 und BR193...");

            var tasks = new List<Task>(2);
            if (_testTrain is not null)
                tasks.Add(_testTrain.EmergencyStopAsync());

            if (tasks.Count == 0)
            {
                Console.WriteLine("[Safety] Nothalt ausgelassen: keine Zuginstanzen initialisiert.");
                return;
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            Console.WriteLine("[Safety] Nothalt ausgefuehrt.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Safety] Fehler beim Nothalt: {ex.Message}");
        }
        finally
        {
            EmergencyStopGate.Release();
        }
    }

    private static void DisposeAccessory(ref Accessory? accessory)
    {
        accessory?.Dispose();
        accessory = null;
    }


    /// <summary>
    ///     Wartet, bis ein Sensor einen bestimmten Zustand erreicht.
    ///     Prüft zuerst den Ist-Zustand, sonst wird auf ein passendes Sensor-Event gewartet.
    /// </summary>
    private static async Task WaitForSensorStateAsync(
        Feedback feedbackModule,
        int sensorNumber,
        RailSensorState targetState,
        CancellationToken cancellationToken)
    {
        var waitStart = DateTimeOffset.Now;
        LogTimed("Feedback", $"WaitForSensorState gestartet: Sensor={sensorNumber}, Ziel={targetState}");

        // Prüfe zunächst den aktuellen Zustand
        var currentState = feedbackModule.GetSensorState(sensorNumber);
        if (currentState == targetState)
        {
            LogTimed("Feedback",
                $"Sensor {sensorNumber} ist bereits {targetState} (ohne Event, +0 ms nach Start).");
            return;
        }

        // Wenn nicht, registriere einen Event-Handler und warte auf die Änderung
        var tcs = new TaskCompletionSource<bool>();
        EventHandler<SensorStateChangedEventArgs>? handler = null;

        handler = (_, args) =>
        {
            if (args.SensorNumber == sensorNumber && args.State == targetState)
            {
                var elapsedMs = (DateTimeOffset.Now - waitStart).TotalMilliseconds;
                LogTimed("Feedback",
                    $"Sensor {sensorNumber} => {args.State} (Event empfangen, +{elapsedMs:F0} ms seit Wait-Start)");
                feedbackModule.SensorStateChanged -= handler;
                tcs.TrySetResult(true);
            }
        };

        feedbackModule.SensorStateChanged += handler;

        try
        {
            // Gib dem CancellationToken eine Chance, wenn vorhanden
            if (cancellationToken != default)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.Token.Register(() => tcs.TrySetCanceled());
                await tcs.Task.ConfigureAwait(false);
            }
            else
            {
                await tcs.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            feedbackModule.SensorStateChanged -= handler;
        }
    }
}