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
using System.Threading;
using System.Threading.Tasks;
using OTD.HardwareControl.Drivers;

namespace OTD.HardwareControl;

internal static class ReadbackTests
{
    /// <summary>
    /// Führt einen Live-Readback-Test für Zubehördecoder-Events der Zentrale aus.
    /// </summary>
    public static async Task ReadBackTestAccessoryAsync(
        CommandStation commandStation,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("=== Accessory ReadBack Live-Test ===");
        Console.WriteLine("Warte auf Accessory-Events von der Zentrale...");
        Console.WriteLine("-> Beliebige Taste drücken zum Beenden.");

        var eventCount = 0;
        commandStation.AccessoryStateChanged += OnAccessoryChanged;
        try
        {
            var keyTask = Task.Run(() => Console.ReadKey(intercept: true), cancellationToken);
            var cancelTask = Task.Delay(Timeout.Infinite, cancellationToken);
            await Task.WhenAny(keyTask, cancelTask).ConfigureAwait(false);
        }
        finally
        {
            commandStation.AccessoryStateChanged -= OnAccessoryChanged;
            Console.WriteLine($"=== Accessory ReadBack beendet (Events: {eventCount}) ===");
        }

        void OnAccessoryChanged(object? _, AccessoryStateChangedEventArgs args)
        {
            var n = Interlocked.Increment(ref eventCount);
            var ts = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.WriteLine(
                $"[{ts}] [CS Accessory ReadBack #{n}] Addr={args.Address}, OutputValue={args.OutputValue}, State={args.State}");
        }
    }

    /// <summary>
    /// Führt den Live-Readback-Test für Lokdecoder aus und zeigt Zentrale- sowie Decoder-Events.
    /// </summary>
    public static async Task ReadBackTestLocoAsync(CommandStation commandStation)
    {
        var trainId = Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6e");
        var train = new Train(trainId, commandStation);

        Console.WriteLine("=== Einlesen Zentrale ===");
        await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        CheckFunctionStates(train);
        await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        Console.WriteLine("=== ReadBack Live-Test ===");
        Console.WriteLine("Abonniere StateChanged-Events auf allen Fahrzeug-Decodern...");

        var commandStationEventCount = 0;
        commandStation.LocoStateChanged += (_, args) =>
        {
            var eventNumber = Interlocked.Increment(ref commandStationEventCount);
            var threadId = Thread.CurrentThread.ManagedThreadId;

            if (args.HasSpeedUpdate)
            {
                Console.WriteLine(
                    $"[CS ReadBack #{eventNumber}] Thread={threadId} Addr={args.Address}: " +
                    $"Speed={args.SpeedStep}, Richtung={args.Direction}, EVT={args.IsEventPacket}");
            }

            if (args.HasFunctionUpdate)
            {
                Console.WriteLine(
                    $"[CS ReadBack #{eventNumber}] Thread={threadId} Addr={args.Address}: " +
                    $"F{args.FunctionNumber}={args.FunctionStateValue}, EVT={args.IsEventPacket}");
            }
        };

        ShowLocoStateChangedResultsContinuously(train);

        Console.WriteLine();
        Console.WriteLine("Warte auf Zustandsänderungen von der Zentrale...");
        Console.WriteLine("(Steuerung extern bedienen, z.B. Geschwindigkeit oder Funktion ändern)");
        Console.WriteLine("-> Beliebige Taste drücken zum Beenden.");
        Console.WriteLine();

        await Task.Run(() => Console.ReadKey(intercept: true)).ConfigureAwait(false);

        Console.WriteLine("=== ReadBack Live-Test beendet ===");
    }

    /// <summary>
    /// Abonniert Decoder-StateChanged-Events aller Fahrzeuge im Zug und protokolliert Änderungen.
    /// </summary>
    public static void ShowLocoStateChangedResultsContinuously(Train train)
    {
        foreach (var entry in train.TrainComposition)
        {
            if (entry.VehicleInstance is not { HasDecoder: true } vehicle)
                continue;

            var vehicleId = entry.VehicleId;
            var decoder = vehicle.LocoDecoder;
            Console.WriteLine($"  -> AccessoryDecoder Adresse {decoder.Address} (Fahrzeug {vehicleId}) subscribed.");

            var vehicleEventCount = 0;

            decoder.StateChanged += (_, args) =>
            {
                var eventNumber = Interlocked.Increment(ref vehicleEventCount);
                var threadId = Thread.CurrentThread.ManagedThreadId;
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

                if (args.HasSpeedUpdate)
                    Console.WriteLine(
                        $"[{timestamp}] [ReadBack #{eventNumber}] Thread={threadId} Addr {args.Address}: " +
                        $"Speed={args.SpeedStep}, Richtung={args.Direction}, EVT={args.IsEventPacket}");

                if (args.HasFunctionUpdate)
                    Console.WriteLine(
                        $"[{timestamp}] [ReadBack #{eventNumber}] Thread={threadId} Addr {args.Address}: " +
                        $"F{args.FunctionNumber}={args.FunctionStateValue}, EVT={args.IsEventPacket}");
            };
        }
    }

    /// <summary>
    /// Abonniert Accessory-Decoder-Readback und schreibt jede Zustandsänderung auf die Konsole.
    /// </summary>
    public static void ShowAccessoryStateChangedResultsContinuously(IAccessoryDecoder accessoryDecoder)
    {
        Console.WriteLine($"  -> AccessoryDecoder Adresse {accessoryDecoder.Address} subscribed.");

        var eventCount = 0;

        accessoryDecoder.StateChanged += (_, args) =>
        {
            var eventNumber = Interlocked.Increment(ref eventCount);
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var state = args.State;

            Console.WriteLine(
                $"[{timestamp}] [Accessory ReadBack #{eventNumber}] Addr {args.Address}: OutputValue {args.OutputValue} = {state}");
        };
    }

    /// <summary>
    /// Gibt den aktuellen Funktions- und Geschwindigkeitsstatus aller Decoder im Zug aus.
    /// </summary>
    public static void CheckFunctionStates(Train train)
    {
        Console.WriteLine("=== Aktuelle Zustände der Fahrzeuge ===");
        foreach (var entry in train.TrainComposition)
        {
            if (entry.VehicleInstance is not { HasDecoder: true } vehicle)
                continue;

            var decoder = vehicle.LocoDecoder;

            Console.WriteLine($"\nFahrzeug: {entry.VehicleId}");
            Console.WriteLine($"  Adresse: {decoder.Address}");

            if (vehicle is Loco loco)
                Console.WriteLine($"  Geschwindigkeit: {loco.Speed} km/h");

            Console.WriteLine("  Funktionszustände:");
            foreach (var func in decoder.Functions)
            {
                var state = decoder.GetFunctionState(func.Number);
                Console.WriteLine($"    F{func.Number}: {state} ({func.Type})");
            }
        }

        Console.WriteLine("\n===========================================\n");
    }
}
