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
using OTD.HardwareControl;
using OTD.TrainDriving.Profiles;

namespace OTD.TrainDriving.Examples;

public class Test1
{
    private const int _vTest = 40; // Test-Geschwindigkeit in km/h
    
    // verwendete Weichen
    private static Accessory? _turnoutW1; // Bi3 -> Bi13
    private static Accessory? _threeWayW5W6;  // Bi3 -> Bi91
    private static Accessory? _turnoutW101; // Abzweigung -> Bi91    
    private static Accessory? _turnoutW102; // Gleiswechsel
    private static Accessory? _turnoutW103; // Gleiswechsel
    private static Accessory? _turnoutW104; // Bi41 - Bi13 (Abzweigung Tiefengrund)
    
    // verwendeter Rückmelder
    private static Feedback? _feedbackModule;

    // verwendete Züge 
    private static Train? _trainBR193;

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

        try
        {
            _feedbackModule = feedbackModule;

            feedbackEventLogger = (_, args) =>
            {
                Console.WriteLine($"[Feedback] {DateTime.Now:HH:mm:ss.fff} Sensor {args.SensorNumber} => {args.State}");
            };
            _feedbackModule.SensorStateChanged += feedbackEventLogger;

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
            _trainBR193 = new Train(Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6e"), commandStation) ??
                          throw new InvalidOperationException("Train BR193 is not initialized.");
            Console.WriteLine("[Train] BR 192 initialisiert");

            // Weichen stellen (für Rundkurs BI)
            Console.WriteLine("W1 -> straight");
            await _turnoutW1.SetStateAsync("straight", emergencyHotkeyCts.Token);
            Console.WriteLine("W5/6 -> right"); // zuerst rechts umstellen wegen Störung Herzstück-Relais
            await _threeWayW5W6.SetStateAsync("right", emergencyHotkeyCts.Token);
            Console.WriteLine("W5/6 -> left");
            await _threeWayW5W6.SetStateAsync("left", emergencyHotkeyCts.Token);
            Console.WriteLine("W101 -> straight");
            await _turnoutW101.SetStateAsync("straight", emergencyHotkeyCts.Token);
            Console.WriteLine("W102 -> diverging");
            await _turnoutW102.SetStateAsync("diverging", emergencyHotkeyCts.Token);
            Console.WriteLine("W103 -> diverging");
            await _turnoutW103.SetStateAsync("diverging", emergencyHotkeyCts.Token);
            Console.WriteLine("W104 -> diverging");
            await _turnoutW104.SetStateAsync("diverging", emergencyHotkeyCts.Token);
            
            // BR193 in Parking-Modus mit Fahrtrichtung A
            _trainBR193.OperatingMode = TrainOperatingMode.Travelling;
            _trainBR193.TrainDirection = TrainDirection.A;
            Console.WriteLine("[Train] OperatingMode=Travelling, TrainDirection=A");

            // Zug mit Ausgangsgeschwindigkeit fahren lassen
            var trainDriving = new TrainDriving(_trainBR193);
            await trainDriving.DriveAsync(
                currentSpeed: 0,
                targetSpeed: _vTest,
                distance: 100,
                // new TrajectoryDelayConfig(1,1),
                cancellationToken: emergencyHotkeyCts.Token);
            
            //await _trainBR193.SetSpeedVAsync(_vTest, emergencyHotkeyCts.Token);
            Console.WriteLine("[TrainDriving] BR193 faehrt.");

            // Warten auf Sensor 56 (Block G91)
            Console.WriteLine("[Feedback] Warte auf Sensor 56 (Block G91) ...");
            await WaitForSensorStateAsync(_feedbackModule, 56, RailSensorState.Active, emergencyHotkeyCts.Token);
            Console.WriteLine("[Feedback] Sensor 56 aktiv -> starte Bremsrampe auf 0 km/h ueber 200 cm.");

            // TrainDriving-Test: lineare Bremsrampe von x auf 0 km/h ueber 200 cm.
            // Scale ist vorlaeufig direkt in TrainDriving als Konstante hinterlegt.

            await trainDriving.DriveAsync(
                currentSpeed: _vTest,
                targetSpeed: 0,
                distance: 200,
                new TrajectoryDelayConfig(0.5, 0.5),
                cancellationToken: emergencyHotkeyCts.Token);

            Console.WriteLine("[TrainDriving] Bremsrampe abgeschlossen: 0 km/h nach 200 cm erreicht.");

            // Zug aus Ziel-Block wegfahren
            Console.WriteLine();
            Console.WriteLine("[TrainDriving] Bremsrampe abgeschlossen: 0 km/h nach 200 cm erreicht. Weiter mit beliebiger Taste.");
            Console.ReadKey(true);
            await _trainBR193.SetSpeedVAsync(20, emergencyHotkeyCts.Token);
            await Task.Delay(TimeSpan.FromSeconds(15), emergencyHotkeyCts.Token);
            await _trainBR193.SetSpeedVAsync(0, emergencyHotkeyCts.Token);
            Console.WriteLine("[TrainDriving] Test abgeschlossen. Zug aus Ziel-Block wegfahren.");
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
            if (_trainBR193 is not null)
                tasks.Add(_trainBR193.EmergencyStopAsync());

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
        // Prüfe zunächst den aktuellen Zustand
        var currentState = feedbackModule.GetSensorState(sensorNumber);
        if (currentState == targetState) return; // Zielzustand bereits erreicht

        // Wenn nicht, registriere einen Event-Handler und warte auf die Änderung
        var tcs = new TaskCompletionSource<bool>();
        EventHandler<SensorStateChangedEventArgs>? handler = null;

        handler = (_, args) =>
        {
            if (args.SensorNumber == sensorNumber && args.State == targetState)
            {
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
