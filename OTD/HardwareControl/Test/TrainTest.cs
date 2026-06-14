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
using OTD.HardwareControl.Drivers;

namespace OTD.HardwareControl;

internal static class TrainTest
{
    /// <summary>
    ///     Entry point for the hardware control console test harness.
    /// </summary>
    public static async Task Main(string[] args)
    {
        var selectedTest = Environment.GetEnvironmentVariable("OTD_TRAIN_TEST");
        if (string.Equals(selectedTest?.Trim(), "FEEDBACK_MOCK_KEYBOARD", StringComparison.OrdinalIgnoreCase))
        {
            await MockKeyboardFeedback.RunAsync();
            return;
        }

        if (Array.Exists(args, a => string.Equals(a, "--test-ensure-operational", StringComparison.OrdinalIgnoreCase)))
        {
            var ipAddress = GetArgValue(args, "--ip") ?? "192.168.1.50";
            var port = int.TryParse(GetArgValue(args, "--port"), out var parsedPort) ? parsedPort : 11092;
            var observeSeconds = int.TryParse(GetArgValue(args, "--observe-seconds"), out var parsedObserveSeconds)
                ? Math.Max(5, parsedObserveSeconds)
                : 30;
            var pulseAddress = int.TryParse(GetArgValue(args, "--pulse-address"), out var parsedPulseAddress)
                ? Math.Clamp(parsedPulseAddress, 1, 2048)
                : 4;
            var pulseValue = byte.TryParse(GetArgValue(args, "--pulse-value"), out var parsedPulseValue)
                ? parsedPulseValue
                : (byte)1;

            await RunEnsureOperationalTriggerTestAsync(
                ipAddress,
                port,
                TimeSpan.FromSeconds(observeSeconds),
                pulseAddress,
                pulseValue);
            return;
        }

        // ── Paarweise Auswahl: Zentrale + zugehöriger Rückmelder ──────────────
        var stationUid        = Guid.Parse("eea1ea06-5c31-428f-8992-1c1d160f1131"); // LoDi Rektor
        var feedbackModuleUid = Guid.Parse("eea1ea06-5c31-428f-8992-1c1d160f1130"); // LoDi S88 Commander
        // var stationUid        = Guid.Parse("00000000-0000-0000-0000-00000000c001"); // MockCommandStation
        // var feedbackModuleUid = Guid.Parse("00000000-0000-0000-0000-00000000f040"); // MockKeyboardFeedback
        // ─────────────────────────────────────────────────────────────────────

        using var commandStation = new CommandStation(stationUid);

        // Verbindung nur aufbauen, wenn noch nicht verbunden
        if (!commandStation.IsConnected)
        {
            await commandStation.ConnectAsync();
        }

        await commandStation.SetPowerAsync(true);
        await Task.Delay(TimeSpan.FromSeconds(3));

        if (!string.IsNullOrWhiteSpace(selectedTest))
        {
            await RunSelectedTrainTestAsync(selectedTest, commandStation, feedbackModuleUid);
            return;
        }

        // Kein automatischer Default-Test: ohne OTD_TRAIN_TEST endet Main nach Basis-Setup.
    }

    private static async Task RunSelectedTrainTestAsync(
        string selectedTest,
        CommandStation commandStation,
        Guid feedbackModuleUid)
    {
        switch (selectedTest.Trim().ToUpperInvariant())
        {
            case "ACCESSORY_EXAMPLES":
            case "ACCESSORY":
                await AccessoryDecoderIntegrationExample.RunAllExamplesAsync(commandStation);
                return;

            case "READBACK_ACCESSORY":
                await ReadBackTest_Accessory(commandStation);
                return;

            case "VT612":
                await VT612_Test(commandStation);
                return;

            case "VT612_UNCOUPLING":
                await VT612_Uncoupling(commandStation);
                return;

            case "DECODER":
                await Decoder_Test(commandStation);
                return;

            case "BR193":
                await BR193_Test(commandStation);
                return;

            case "READBACK_LOCO":
                await ReadBackTest_Loco(commandStation);
                return;

            case "FEEDBACK":
                await FeedbackTest.RunSingleModuleAsync(feedbackModuleUid);
                return;

            case "FEEDBACK_MOCK_KEYBOARD":
                await MockKeyboardFeedback.RunAsync();
                return;

            default:
                throw new InvalidOperationException($"Unknown OTD_TRAIN_TEST: {selectedTest}");
        }
    }

    public static async Task ReadBackTest_Accessory(CommandStation commandStation)
    {
        // var w1Uid  = Guid.Parse("3f8a1b2c-4d5e-4f7a-8b9c-0d1e2f3a4b5c");
        // var turnout = new Accessory.Accessory(w1Uid);
        // await turnout.SubscribeCommandStationAsync(commandStation);
        //
        // foreach (var stateId in turnout.GetAvailableStateIds())
        // {
        //     await turnout.SetStateAsync(stateId);
        //     Console.WriteLine($"  W1 CurrentState: {turnout.CurrentState}");
        //     await Task.Delay(2000); // Kurze Pause zwischen den Zustandswechseln
        // }

        await Task.Delay(TimeSpan.FromSeconds(30));
    }
    
    public static async Task VT612_Test(CommandStation commandStation)
    {
        var trainId = Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6c");
        var train = new Train(trainId, commandStation);
        Console.WriteLine("1---------");
        await train.SetFunctionStateAsync(0, LocoDecoderFunctionState.On, [train.TrainComposition[0].VehicleId]);
        train.OperatingMode = TrainOperatingMode.Parking;
        //train.HeadlightMode = HeadlightMode.Auto;
        Console.WriteLine("2---------");
        await Task.Delay(TimeSpan.FromSeconds(7));
        train.TrainDirection = TrainDirection.A;
        train.OperatingMode = TrainOperatingMode.Travelling;
        Console.WriteLine("3---------");
        await Task.Delay(TimeSpan.FromSeconds(7));
        await train.SetSpeedVAsync(40);
        Console.WriteLine("4---------");
        // 10 Sekunden warten
        await Task.Delay(TimeSpan.FromSeconds(10));
        await train.SetSpeedVAsync(0);
        Console.WriteLine("5---------");
        // 3 Sekunden warten
        await Task.Delay(TimeSpan.FromSeconds(7));
        train.TrainDirection = TrainDirection.B;
        Console.WriteLine("6---------");
        await train.SetSpeedVAsync(7);
        Console.WriteLine("7---------");
        // 10 Sekunden warten
        await Task.Delay(TimeSpan.FromSeconds(10));
        await train.SetSpeedVAsync(0);
        Console.WriteLine("8---------");
        // 5 Sekunden warten
        await Task.Delay(TimeSpan.FromSeconds(7));
        train.OperatingMode = TrainOperatingMode.Parking;
        train.HeadlightMode = HeadlightMode.Off;
        Console.WriteLine("9---------");
    }

    public static async Task VT612_Uncoupling(CommandStation commandStation)
    {
        var trainId = Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6c");
        var train = new Train(trainId, commandStation);

        // Komposition fuer Konfigurationsoperationen aus aktiver Train-Instanz loesen.
        var builder = await train.DetachCompositionAsync();

        // Trennung zwischen den beiden VT612-Loks ausfuehren.
        var (sourceTrainId, newTrainId) = await builder.SplitCompositionAsync(
            Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d481"),
            Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d482"));

        Console.WriteLine("Beliebige Taste drücken, um fortzufahren...");
        Console.ReadKey(intercept: true);

        builder.JoinComposition(
            sourceTrainId,
            newTrainId);

    }

    public static async Task Decoder_Test(CommandStation commandStation)
    {
        var trainId = Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6e");
        var train = new Train(trainId, commandStation);

        await Task.Delay(TimeSpan.FromSeconds(7));
        await train.SetFunctionStateAsync(27, LocoDecoderFunctionState.On, [train.TrainComposition[0].VehicleId]);
        await train.SetFunctionStateAsync(0, LocoDecoderFunctionState.On, [train.TrainComposition[0].VehicleId]);
        await Task.Delay(TimeSpan.FromSeconds(7));
        await train.SetFunctionStateAsync(0, LocoDecoderFunctionState.Off, [train.TrainComposition[0].VehicleId]);
        await train.SetFunctionStateAsync(27, LocoDecoderFunctionState.Off, [train.TrainComposition[0].VehicleId]);
        await Task.Delay(TimeSpan.FromSeconds(7));
    }

    public static async Task BR193_Test(CommandStation commandStation)
    {
        var trainId = Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6e");
        var train = new Train(trainId, commandStation);

        Console.WriteLine("---------");
        CheckFunctionStates(train);
        // Console.WriteLine("→ Beliebige Taste drücken zum Beenden...");
        // Console.ReadKey(intercept: true);

        train.HeadlightMode = HeadlightMode.Auto;
        train.OperatingMode = TrainOperatingMode.Parking;
        Console.WriteLine("→ Nach OperatingMode=Parking gesetzt");
//        await train.SetFunctionStateAsync(27, FunctionState.On, [train.TrainComposition[0].VehicleId]);
//        await train.SetFunctionStateAsync(0, FunctionState.On, [train.TrainComposition[0].VehicleId]);
        await Task.Delay(TimeSpan.FromSeconds(60));
        Console.WriteLine("→ Nach Task.Delay");
        CheckFunctionStates(train);
        Console.WriteLine("---------");
        train.OperatingMode = TrainOperatingMode.Shunting;
        train.TrainDirection = TrainDirection.A;
        await train.SetSpeedVAsync(7);
        await Task.Delay(TimeSpan.FromSeconds(7));
        await train.SetSpeedVAsync(0);
        Console.WriteLine("---------");
        train.TrainDirection = TrainDirection.B;
        await train.SetSpeedVAsync(7);
        await Task.Delay(TimeSpan.FromSeconds(7));
        await train.SetSpeedVAsync(0);
        Console.WriteLine("---------");
        train.OperatingMode = TrainOperatingMode.Parking;
        await Task.Delay(TimeSpan.FromSeconds(7));
        train.OperatingMode = TrainOperatingMode.ShutDown;
    }

    /// <summary>
    ///     Tests accessory decoder readback with a real LoDi connection.
    ///     Subscribes to StateChanged events on all vehicle decoders and prints
    ///     all incoming state changes from the command station to the console.
    ///     Runs until a key is pressed.
    /// </summary>
    public static async Task ReadBackTest_Loco(CommandStation commandStation)
    {
        var trainId = Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6e");
        var train = new Train(trainId, commandStation);

        Console.WriteLine("=== Einlesen Zentrale ===");
        await Task.Delay(TimeSpan.FromSeconds(4));        
        CheckFunctionStates(train);
        await Task.Delay(TimeSpan.FromSeconds(4)); 
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
        Console.WriteLine("→ Beliebige Taste drücken zum Beenden.");
        Console.WriteLine();

        // Warte asynchron, bis eine Taste gedrückt wird
        await Task.Run(() => Console.ReadKey(intercept: true));

        Console.WriteLine("=== ReadBack Live-Test beendet ===");
    }

    public static void ShowLocoStateChangedResultsContinuously(Train train)
    {
        foreach (var entry in train.TrainComposition)
        {
            if (entry.VehicleInstance is not { HasDecoder: true } vehicle)
                continue;

            var vehicleId = entry.VehicleId;
            var decoder = vehicle.LocoDecoder;
            Console.WriteLine($"  → AccessoryDecoder Adresse {decoder.Address} (Fahrzeug {vehicleId}) subscribed.");

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

    
    public static void ShowAccessoryStateChangedResultsContinuously(IAccessoryDecoder accessoryDecoder)
    {
        Console.WriteLine($"  → AccessoryDecoder Adresse {accessoryDecoder.Address} subscribed.");

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

    public static void CheckFunctionStates(Train train)
    {
        // Ausgabe der aktuellen Funktions-Zustände und Geschwindigkeit aller Fahrzeuge
        Console.WriteLine("=== Aktuelle Zustände der Fahrzeuge ===");
        foreach (var entry in train.TrainComposition)
        {
            if (entry.VehicleInstance is not { HasDecoder: true } vehicle)
                continue;

            var decoder = vehicle.LocoDecoder;

            Console.WriteLine($"\nFahrzeug: {entry.VehicleId}");
            Console.WriteLine($"  Adresse: {decoder.Address}");

            if (vehicle is Loco loco)
            {
                Console.WriteLine($"  Geschwindigkeit: {loco.Speed} km/h");
            }

            Console.WriteLine("  Funktionszustände:");
            foreach (var func in decoder.Functions)
            {
                var state = decoder.GetFunctionState(func.Number);
                Console.WriteLine($"    F{func.Number}: {state} ({func.Type})");
            }
        }
        Console.WriteLine("\n===========================================\n");
    }

    public static async Task RunEnsureOperationalTriggerTestAsync(
        string ipAddress,
        int port,
        TimeSpan observeWindow,
        int pulseAddress,
        byte pulseValue)
    {
        Console.WriteLine("=== Diagnose: Trigger fuer eintreffende Rueckmeldungen ===");
        Console.WriteLine($"Ziel: {ipAddress}:{port}, Beobachtungsfenster: {observeWindow.TotalSeconds:0}s");
        Console.WriteLine($"Deterministischer Impuls: Accessory Addr={pulseAddress}, Value={pulseValue} (On->Off)");
        Console.WriteLine();

        await RunScenarioAsync(
            "A: Baseline (nur Connect + Warten)",
            ipAddress,
            port,
            observeWindow,
            pulseAddress,
            pulseValue,
            (_, _) => Task.CompletedTask);

        await RunScenarioAsync(
            "B: GetPowerStateAsync",
            ipAddress,
            port,
            observeWindow,
            pulseAddress,
            pulseValue,
            async (cs, ct) => _ = await cs.GetPowerStateAsync(ct).ConfigureAwait(false));

        await RunScenarioAsync(
            "C: SetPowerAsync(true)",
            ipAddress,
            port,
            observeWindow,
            pulseAddress,
            pulseValue,
            async (cs, ct) => await cs.SetPowerAsync(true, ct).ConfigureAwait(false));

        await RunScenarioAsync(
            "D: EnsureOperationalAsync",
            ipAddress,
            port,
            observeWindow,
            pulseAddress,
            pulseValue,
            async (cs, ct) => _ = await cs.EnsureOperationalAsync(ct).ConfigureAwait(false));

        Console.WriteLine("=== Diagnose beendet ===");
    }

    private static async Task RunScenarioAsync(
        string title,
        string ipAddress,
        int port,
        TimeSpan observeWindow,
        int pulseAddress,
        byte pulseValue,
        Func<CommandStation, CancellationToken, Task> trigger)
    {
        using var scenarioCts = new CancellationTokenSource();

        var stationUid = Guid.Parse("eea1ea06-5c31-428f-8992-1c1d160f1131"); // LoDi Rektor
        // var stationUid = Guid.Parse("00000000-0000-0000-0000-00000000c001"); // MockCommandStation
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

    private static string? GetArgValue(IReadOnlyList<string> args, string key)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
