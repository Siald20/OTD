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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OTD.HardwareControl;

internal static class FeedbackTests
{
    /// <summary>
    ///     Führt einen interaktiven Einzelmodul-Test aus (Connect, Snapshot, Live-Monitoring).
    /// </summary>
    public static async Task RunSingleModuleAsync(
        Guid moduleUid,
        CancellationToken cancellationToken = default)
    {
        using var module = new Feedback(moduleUid);

        Console.WriteLine($"=== FeedbackTest - Einzelmodul {moduleUid} ===");
        Console.WriteLine($"Treiber : {module.DriverName}");

        Console.WriteLine("\n[1/3] Verbinde...");
        await module.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var isReady = await module.EnsureOperationalAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine(
            $"      Verbunden: {module.IsConnected}  |  Operational: {isReady}  |  Sensoren: {module.SensorCount}");

        Console.WriteLine("\n[2/3] Sensor-Snapshot abfragen...");
        var snapshot = await module.QueryAllSensorsAsync(cancellationToken).ConfigureAwait(false);
        PrintSnapshot(moduleUid, snapshot, module.SensorCount);

        Console.WriteLine("\n[3/3] Monitoring aktiv - Taste druecken zum Beenden.");
        var eventCount = 0;
        module.SensorStateChanged += OnChanged;

        await WaitForKeyOrCancelAsync(cancellationToken).ConfigureAwait(false);

        module.SensorStateChanged -= OnChanged;
        Console.WriteLine($"\nMonitoring beendet. Empfangene Updates: {eventCount}");

        await module.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine("Verbindung getrennt.");
        return;

        void OnChanged(object? _, SensorStateChangedEventArgs e)
        {
            var n = Interlocked.Increment(ref eventCount);
            var mark = e.State == RailSensorState.Active ? "ON " : "OFF";
            Console.WriteLine(
                $"  [{DateTimeOffset.Now:HH:mm:ss.fff}] #{n:D4}  Sensor {e.SensorNumber:D4}  {mark}");
        }
    }

    /// <summary>
    ///     Startet eine Diagnose mit mehreren Trigger-Szenarien und vergleicht eingehende Rückmeldungen.
    /// </summary>
    public static async Task RunEnsureOperationalTriggerTestAsync(
        Guid stationUid,
        TimeSpan observeWindow,
        int pulseAddress,
        byte pulseValue)
    {
        Console.WriteLine("=== Diagnose: Trigger fuer eintreffende Rueckmeldungen ===");
        Console.WriteLine($"Station UID: {stationUid}");
        Console.WriteLine($"Beobachtungsfenster: {observeWindow.TotalSeconds:0}s");
        Console.WriteLine($"Deterministischer Impuls: Accessory Addr={pulseAddress}, Value={pulseValue} (On->Off)");
        Console.WriteLine();

        await RunScenarioAsync(
            "A: Baseline (nur Connect + Warten)",
            stationUid,
            observeWindow,
            pulseAddress,
            pulseValue,
            (_, _) => Task.CompletedTask);

        await RunScenarioAsync(
            "B: GetPowerStateAsync",
            stationUid,
            observeWindow,
            pulseAddress,
            pulseValue,
            async (cs, ct) => _ = await cs.GetPowerStateAsync(ct).ConfigureAwait(false));

        await RunScenarioAsync(
            "C: SetPowerAsync(true)",
            stationUid,
            observeWindow,
            pulseAddress,
            pulseValue,
            async (cs, ct) => await cs.SetPowerAsync(true, ct).ConfigureAwait(false));

        await RunScenarioAsync(
            "D: EnsureOperationalAsync",
            stationUid,
            observeWindow,
            pulseAddress,
            pulseValue,
            async (cs, ct) => _ = await cs.EnsureOperationalAsync(ct).ConfigureAwait(false));

        Console.WriteLine("=== Diagnose beendet ===");
    }

    /// <summary>
    ///     Führt ein einzelnes Trigger-Szenario aus und protokolliert die erste/gesamt empfangene Rückmeldung.
    /// </summary>
    private static async Task RunScenarioAsync(
        string title,
        Guid stationUid,
        TimeSpan observeWindow,
        int pulseAddress,
        byte pulseValue,
        Func<CommandStation, CancellationToken, Task> trigger)
    {
        using var scenarioCts = new CancellationTokenSource();

        using var commandStation = new CommandStation(stationUid);

        var locoEvents = 0;
        var accessoryEvents = 0;
        var firstEventInfo = "keine Rueckmeldung innerhalb Beobachtungsfenster";
        var firstEventCaptured = false;

        commandStation.LocoStateChanged += (_, args) =>
        {
            var count = Interlocked.Increment(ref locoEvents);
            if (firstEventCaptured)
                return;

            firstEventCaptured = true;
            firstEventInfo = args.HasSpeedUpdate
                ? $"LocoEvent #{count}: Addr={args.Address}, Speed={args.SpeedStep}, Dir={args.Direction}, EVT={args.IsEventPacket}"
                : $"LocoEvent #{count}: Addr={args.Address}, F{args.FunctionNumber}={args.FunctionStateValue}, EVT={args.IsEventPacket}";
        };

        commandStation.AccessoryStateChanged += (_, args) =>
        {
            var count = Interlocked.Increment(ref accessoryEvents);
            if (firstEventCaptured)
                return;

            firstEventCaptured = true;
            firstEventInfo =
                $"AccessoryEvent #{count}: Addr={args.Address}, OutputValue={args.OutputValue}, State={args.State}";
        };

        Console.WriteLine($"--- {title} ---");

        try
        {
            await commandStation.ConnectAsync(scenarioCts.Token).ConfigureAwait(false);
            await trigger(commandStation, scenarioCts.Token).ConfigureAwait(false);

            Console.WriteLine(
                $"Sende Testimpuls: Addr={pulseAddress}, Value={pulseValue} (On->Off), Protocol=Dcc");
            await SendAccessoryPulseAsync(commandStation, pulseAddress, pulseValue, scenarioCts.Token)
                .ConfigureAwait(false);

            await Task.Delay(observeWindow, scenarioCts.Token).ConfigureAwait(false);

            Console.WriteLine($"Ergebnis: LocoEvents={locoEvents}, AccessoryEvents={accessoryEvents}");
            Console.WriteLine($"Erste Rueckmeldung: {firstEventInfo}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Szenariofehler: {ex.Message}");
        }
        finally
        {
            try
            {
                await commandStation.DisconnectAsync().ConfigureAwait(false);
            }
            catch
            {
                // Verbindungsabbau soll die Diagnose nicht unterbrechen.
            }
        }

        Console.WriteLine();
    }

    /// <summary>
    ///     Sendet einen kurzen Accessory-Impuls (On -> Off) als deterministischen Testtrigger.
    /// </summary>
    private static async Task SendAccessoryPulseAsync(
        CommandStation commandStation,
        int address,
        byte value,
        CancellationToken cancellationToken)
    {
        await commandStation.SetAccessoryValueAsync(
                address,
                value,
                AccessoryDecoderProtocol.Dcc,
                AccessoryFunctionState.On,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);

        await commandStation.SetAccessoryValueAsync(
                address,
                value,
                AccessoryDecoderProtocol.Dcc,
                AccessoryFunctionState.Off,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Gibt einen kompakten Sensor-Snapshot aus und listet aktive Sensoren explizit auf.
    /// </summary>
    private static void PrintSnapshot(
        Guid moduleUid,
        IReadOnlyDictionary<int, RailSensorState> states,
        int sensorCount)
    {
        var bar = string.Concat(
            Enumerable.Range(1, sensorCount)
                .Select(n => states.TryGetValue(n, out var s) && s == RailSensorState.Active ? 'X' : '.'));

        Console.WriteLine($"  Modul {moduleUid}  [{bar}]");

        var active = states
            .Where(kv => kv.Value == RailSensorState.Active)
            .OrderBy(kv => kv.Key)
            .ToList();

        if (active.Count == 0)
            Console.WriteLine("    (alle Sensoren frei)");
        else
            foreach (var (nr, _) in active)
                Console.WriteLine($"    Sensor {nr:D4}: belegt");
    }

    /// <summary>
    ///     Wartet bis Tastendruck oder Abbruchsignal eintritt.
    /// </summary>
    private static Task WaitForKeyOrCancelAsync(CancellationToken cancellationToken)
    {
        var keyTask = Task.Run(() => Console.ReadKey(true), cancellationToken);
        var cancelTask = Task.Delay(Timeout.Infinite, cancellationToken);
        return Task.WhenAny(keyTask, cancelTask);
    }
}