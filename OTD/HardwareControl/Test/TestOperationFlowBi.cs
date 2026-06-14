// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - DecoderControl
// Copyright (C) 2026
//
// Authors:
// - Hansueli Alder <info@batec.net>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OTD.HardwareControl.Test;

internal static class TestOperationFlowBi
{
    // verwendete Weichen
    private static Accessory? _turnoutW1;
    private static Accessory? _dkwW2;
    private static Accessory? _turnoutW3;
    private static Accessory? _turnoutW4;
    private static Accessory? _threeWayW5W6;

    // verwendeter Rückmelder
    private static Feedback? _feedbackModule;

    // verwendete Züge 
    private static Train? _trainDt612;
    private static Train? _trainBR193;

    private static readonly SemaphoreSlim EmergencyStopGate = new(1, 1);
    private const int SpeedStepKmH = 5;
    private static readonly TimeSpan SpeedStepInterval = TimeSpan.FromSeconds(1);

    public static void Run(CommandStation commandStation, Feedback feedbackModule)
    {
        ArgumentNullException.ThrowIfNull(commandStation);
        ArgumentNullException.ThrowIfNull(feedbackModule);

        RunAsync(commandStation, feedbackModule).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(CommandStation commandStation, Feedback feedbackModule)
    {
        using var emergencyHotkeyCts = new CancellationTokenSource();
        var emergencyHotkeyTask = StartEmergencyStopHotkeyListenerAsync(emergencyHotkeyCts.Token);

        try
        {
            _feedbackModule = feedbackModule;

            // Weichen initialisieren
            _turnoutW1 = new Accessory(Guid.Parse("3f8a1b2c-4d5e-4f7a-8b9c-0d1e2f3a4b5c"), commandStation) ??
                         throw new InvalidOperationException("Turnout W1 not initialized.");
            _dkwW2 = new Accessory(Guid.Parse("4a9b2c3d-5e6f-4a7b-9c0d-1e2f3a4b5c6d"), commandStation) ??
                     throw new InvalidOperationException("Turnout W2 not initialized.");
            _turnoutW3 = new Accessory(Guid.Parse("b2f3eaa9-43f9-414e-997c-92d44406a63b"), commandStation) ??
                         throw new InvalidOperationException("Turnout W3 not initialized.");
            _turnoutW4 = new Accessory(Guid.Parse("bbcc3ec1-25ee-44d5-b086-6a13e33c548f"), commandStation) ??
                         throw new InvalidOperationException("Turnout W4 not initialized.");
            _threeWayW5W6 = new Accessory(Guid.Parse("5b0c3d4e-6f7a-4b8c-9d0e-2f3a4b5c6d7e"), commandStation) ??
                            throw new InvalidOperationException("Turnout W5/6 not initialized.");

            // Züge initialisieren
            _trainDt612 = new Train(Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6c"), commandStation) ??
                          throw new InvalidOperationException("Train VT612 is not initialized.");
            Console.WriteLine("[Train] VT 612 initialisiert");
            _trainBR193 = new Train(Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6e"), commandStation) ??
                          throw new InvalidOperationException("Train BR193 is not initialized.");
            Console.WriteLine("[Train] BR 192 initialisiert");

            // DT612 in Travelling-Modus mit Fahrtrichtung A
            _trainDt612.OperatingMode = TrainOperatingMode.Travelling;
            _trainDt612.TrainDirection = TrainDirection.A;
            Console.WriteLine("[Train] OperatingMode=Travelling, TrainDirection=A");

            // BR193 in Parking-Modus mit Fahrtrichtung A
            _trainBR193.OperatingMode = TrainOperatingMode.Parking;
            _trainBR193.TrainDirection = TrainDirection.A;
            Console.WriteLine("[Train] OperatingMode=Parking, TrainDirection=A");

            // Beide Abläufe parallel starten (nicht blockierend) und gemeinsam abwarten.
            var dt612Task = Dt612Async(emergencyHotkeyCts.Token);
            var br193Task = BR193Async(emergencyHotkeyCts.Token);
            await Task.WhenAll(dt612Task, br193Task).ConfigureAwait(false);
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

            DisposeAccessory(ref _turnoutW1);
            DisposeAccessory(ref _dkwW2);
            DisposeAccessory(ref _turnoutW3);
            DisposeAccessory(ref _turnoutW4);
            DisposeAccessory(ref _threeWayW5W6);
            _feedbackModule = null;
        }
    }

    private static async Task DriveDt612Async(int speed)
    {
        if (_trainDt612 is null)
            throw new InvalidOperationException("Train VT612 is not initialized.");

        // Fahrtbefehle immer über die gemeinsame Rampenlogik schicken.
        await SetSpeedGraduallyAsync(_trainDt612, speed, SpeedStepKmH, SpeedStepInterval);
    }

    private static async Task DriveBR193Async(int speed)
    {
        if (_trainBR193 is null)
            throw new InvalidOperationException("Train BR193 is not initialized.");

        // Fahrtbefehle immer über die gemeinsame Rampenlogik schicken.
        await SetSpeedGraduallyAsync(_trainBR193, speed, SpeedStepKmH, SpeedStepInterval);
    }

    private static async Task Dt612Async(CancellationToken cancellationToken)
    {
        // Ausfahrt Bi2 -> Bi13 stellen
        Console.WriteLine("W2 -> crossing-straight");
        await _dkwW2.SetStateAsync("crossing-straight");
        Console.WriteLine("W1 -> diverging");
        await _turnoutW1.SetStateAsync("diverging");

        // Nach 10 Sekunden Ausfahrt mit 40 km/h
        await Task.Delay(TimeSpan.FromSeconds(10));
        await DriveDt612Async(40);

        // Warten auf Sensor-Sequenz: Sensor 31 aktiv -> Sensor 30 aktiv -> Sensor 31 inaktiv
        // => W1 frei
        Console.WriteLine("[Feedback] Warte auf Sensor 31 (aktiv)...");
        await WaitForSensorStateAsync(_feedbackModule, 31, RailSensorState.Active, CancellationToken.None);
        Console.WriteLine("[Feedback] Sensor 31 aktiv! Warte auf Sensor 30 (aktiv)...");
        await WaitForSensorStateAsync(_feedbackModule, 30, RailSensorState.Active, CancellationToken.None);
        Console.WriteLine("[Feedback] Sensor 30 aktiv! Warte auf Sensor 31 (inaktiv) - Zug verlässt W1...");
        await WaitForSensorStateAsync(_feedbackModule, 31, RailSensorState.Inactive, CancellationToken.None);
        Console.WriteLine("[Feedback] Sensor 31 inaktiv - VT612 hat W1 verlassen!");

        // Nach vollständiger Überfahrt über W1 Fahrt mit 60 km/h
        await DriveDt612Async(60);

        // Ankunft in Bi91
        await WaitForSensorStateAsync(_feedbackModule, 56, RailSensorState.Active, CancellationToken.None);
        Console.WriteLine("[Feedback] Sensor 56 aktiv - VT612 in Bi91 eingetroffen!");

        // Einfahrt Bi91 -> Bi2 stellen (VT612)
        Console.WriteLine("W5/6 -> straight");
        await _threeWayW5W6.SetStateAsync("straight");

        // Warten auf Sensor 29 (Bestztmeldung W5/6)
        await WaitForSensorStateAsync(_feedbackModule, 54, RailSensorState.Active, CancellationToken.None);
        Console.WriteLine("[Feedback] Sensor 54 aktiv - VT612 beführt W5/6!");

        // 2 Sekunden verzögern, Abbremsen, Anhalten
        await Task.Delay(TimeSpan.FromSeconds(3));
        await DriveDt612Async(0);

        // 5 Sekunden warten, Parkmodus einstellen
        await Task.Delay(TimeSpan.FromSeconds(5));
        _trainDt612.OperatingMode = TrainOperatingMode.Parking;

        Console.WriteLine("***** Zugfahrt VT612 beendet. *****");
    }

    private static async Task BR193Async(CancellationToken cancellationToken)
    {
        // Warten auf Sensor-Sequenz: Senser 30 aktiv -> Sensor 30 inaktiv (VT 612 verlässt Bi13)
        Console.WriteLine("[Feedback] Sensor 30 inaktiv! - Zug hat Bi13 verlassen...");
        await WaitForSensorStateAsync(_feedbackModule, 30, RailSensorState.Active, CancellationToken.None);
        await WaitForSensorStateAsync(_feedbackModule, 30, RailSensorState.Inactive, CancellationToken.None);

        // Ausfahrt Bi1 -> Bi13 stellen
        Console.WriteLine("W3 -> diverging");
        await _turnoutW3.SetStateAsync("diverging");
        Console.WriteLine("W2 -> crossing");
        await _dkwW2.SetStateAsync("crossing");

        // Rangiermodus einstellen
        _trainBR193.OperatingMode = TrainOperatingMode.Shunting;

        // nach 2 Sekunden Fahrt mit 40 km/h
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        await DriveBR193Async(40);

        // Warten auf Sensor-Sequenz W2: Sensor 28 aktiv -> Sensor 28 inaktiv
        // -> Zug hat W2 verlassen, danach Start Bremsvorgang, um nach W1 zum Stehen zu kommen.
        Console.WriteLine("[Feedback] Warte auf Sensor 28 (aktiv)...");
        await WaitForSensorStateAsync(_feedbackModule, 28, RailSensorState.Active, CancellationToken.None);
        Console.WriteLine("[Feedback] Sensor 28 aktiv! Warte auf Sensor 28 inaktiv...");
        await WaitForSensorStateAsync(_feedbackModule, 28, RailSensorState.Inactive, CancellationToken.None);
        Console.WriteLine("[Feedback] Sensor 28 inaktiv! Zug hat W2 verlassen...");

        // bremsen und anhalten
        await DriveBR193Async(0);
        // sicherstellen, dass W1 frei ist.
        Console.WriteLine("[Feedback] Warte auf Sensor 31 (inaktiv)...");
        await WaitForSensorStateAsync(_feedbackModule, 31, RailSensorState.Inactive, CancellationToken.None);
        Console.WriteLine("[Feedback] Sensor 31 inaktiv - BR193 hat W1 verlassen!");
        
        // Warten auf Sensor-Sequenz: Sensor 54 aktiv -> Sensor 52 aktiv -> Sensor 54 inaktiv
        // => Warteun auf Ankunft VT 612 in Bi2, sicherstellen, dass W5/6 frei sind.
        Console.WriteLine("[Feedback] Warte auf Sensor 54 (aktiv)...");
        await WaitForSensorStateAsync(_feedbackModule, 54, RailSensorState.Active, CancellationToken.None);
        Console.WriteLine("[Feedback] Sensor 54 aktiv! Warte auf Sensor 52 (aktiv)...");
        await WaitForSensorStateAsync(_feedbackModule, 52, RailSensorState.Active, CancellationToken.None);
        Console.WriteLine("[Feedback] Sensor 52 aktiv! Warte auf Sensor 54 (inaktiv) - Zug verlässt W1...");
        await WaitForSensorStateAsync(_feedbackModule, 54, RailSensorState.Inactive, CancellationToken.None);
        Console.WriteLine("[Feedback] Sensor 31 inaktiv - BR193 hat W1 verlassen!");

        // Durchfahrt Bi13 -> Bi3 -> B91 stellen
        Console.WriteLine("W1 -> straight");
        await _turnoutW1.SetStateAsync("straight");
        Console.WriteLine("W5/6 -> left");
        await _threeWayW5W6.SetStateAsync("left");

        // Fahrtrichtung wechseln
        _trainBR193.TrainDirection = TrainDirection.B;
        // Nach 5 Sekunden Fahrt mit 40 km/h 
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        await DriveBR193Async(40);

        // Warten auf Sensor-Sequenz: Sensor 54 aktiv -> Sensor 30 aktiv -> Sensor 31 inaktiv
        // => Zug hat Bi3 verlassen, danach Start Bremsvorgang, um nach W5/6 zum Stehen zu kommen.
        Console.WriteLine("[Feedback] Warte auf Sensor 53 (aktiv)...");
        await WaitForSensorStateAsync(_feedbackModule, 53, RailSensorState.Active, CancellationToken.None);
        Console.WriteLine("[Feedback] Sensor 53 aktiv! Warte auf Sensor 53 (inaktiv)...");
        await WaitForSensorStateAsync(_feedbackModule, 53, RailSensorState.Inactive, CancellationToken.None);
        Console.WriteLine("[Feedback] Sensor 53 inaktiv! Zug hat Bi3 verlassen.");

        // bremsen und anhalten
        await DriveBR193Async(0);
        
        // sicherstellen, dass W5/6 frei ist.
        Console.WriteLine("[Feedback] Warte auf Sensor 54 (inaktiv)...");
        await WaitForSensorStateAsync(_feedbackModule, 54, RailSensorState.Inactive, CancellationToken.None);
        Console.WriteLine("[Feedback] Sensor 54 inaktiv - Zug hat W5/6 verlassen!");
        // 10 Sekunden warten
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

        // Einfahrt Bi91 -> Bi1 stellen
        Console.WriteLine("[Flow] 8) W5/6 -> right");
        await _threeWayW5W6.SetStateAsync("right");
        Console.WriteLine("[Flow] 9 W4 -> straight");
        await _turnoutW4.SetStateAsync("straight");

        // Fahrtrichtung wechseln, 5 Sekunden warten
        _trainBR193.TrainDirection = TrainDirection.A;
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

        // Fahrt mit 40 km/h
        await DriveBR193Async(40);

        // Warten auf Sensor 49 (Bestztmeldung Bi1)
        await WaitForSensorStateAsync(_feedbackModule, 49, RailSensorState.Active, CancellationToken.None);

        // in Bi1 angekommen: nach 4 s Verzögerung anhalten
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        await DriveBR193Async(0);

        // 5 Sekunden warten, Parkmodus einstellen
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        _trainBR193.OperatingMode = TrainOperatingMode.Parking;

        Console.WriteLine("***** Zugfahrt BR193 beendet. *****");
    }

    /// <summary>
    /// Setzt die Zuggeschwindigkeit in gleichmäßigen Schritten (Beschleunigen und Abbremsen).
    /// </summary>
    private static async Task SetSpeedGraduallyAsync(
        Train train,
        int targetSpeed,
        int stepKmH,
        TimeSpan stepInterval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(train);
        if (stepKmH <= 0)
            throw new ArgumentOutOfRangeException(nameof(stepKmH), "Step must be greater than zero.");

        var currentSpeed = train.SpeedV;
        if (currentSpeed == targetSpeed)
            return;

        var delta = targetSpeed - currentSpeed;
        var direction = Math.Sign(delta);

        // In festen Schritten bis zur Zielgeschwindigkeit beschleunigen/abbremsen.
        while (currentSpeed != targetSpeed)
        {
            var nextSpeed = currentSpeed + (direction * stepKmH);
            if ((direction > 0 && nextSpeed > targetSpeed) || (direction < 0 && nextSpeed < targetSpeed))
                nextSpeed = targetSpeed;

            await train.SetSpeedVAsync(nextSpeed, cancellationToken).ConfigureAwait(false);
            currentSpeed = nextSpeed;

            if (currentSpeed != targetSpeed)
                await Task.Delay(stepInterval, cancellationToken).ConfigureAwait(false);
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

                var keyInfo = Console.ReadKey(intercept: true);
                if (keyInfo.Key != ConsoleKey.Spacebar)
                    continue;

                await EmergencyStopBothTrainsAsync().ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    // Löst eine Notbremsung für beide Züge aus (mit Leertaste)
    private static async Task EmergencyStopBothTrainsAsync()
    {
        // Gate verhindert parallele Mehrfachauslösungen bei schneller Tasteneingabe.
        await EmergencyStopGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Console.WriteLine("[Safety] SPACE erkannt -> Nothalt fuer VT612 und BR193...");

            var tasks = new List<Task>(capacity: 2);
            if (_trainDt612 is not null)
                tasks.Add(_trainDt612.EmergencyStopAsync());
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
    /// Wartet, bis ein Sensor einen bestimmten Zustand erreicht.
    /// Prüft zuerst den Ist-Zustand, sonst wird auf ein passendes Sensor-Event gewartet.
    /// </summary>
    private static async Task WaitForSensorStateAsync(
        Feedback feedbackModule,
        int sensorNumber,
        RailSensorState targetState,
        CancellationToken cancellationToken)
    {
        // Prüfe zunächst den aktuellen Zustand
        var currentState = feedbackModule.GetSensorState(sensorNumber);
        if (currentState == targetState)
        {
            return; // Zielzustand bereits erreicht
        }

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
