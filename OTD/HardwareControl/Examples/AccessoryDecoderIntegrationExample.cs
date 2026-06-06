// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - Accessory Integration Example
// Copyright (C) 2026

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessoryItem = OTD.HardwareControl.Accessory.Accessory;
using AccessoryStateChangedEventArgs = OTD.HardwareControl.Accessory.AccessoryStateChangedEventArgs;
using OTD.HardwareControl.CommandStation;

namespace OTD.HardwareControl.Examples;

/// <summary>
/// Practical examples for the high-level <see cref="AccessoryItem"/> API.
/// Demonstrates the subscribe/unsubscribe model for command stations,
/// analogous to the locomotive decoder architecture.
/// </summary>
public static class AccessoryDecoderIntegrationExample
{
    // UIDs from OTD/AppData/accessory.xml
    private static readonly Guid W1Uid  = Guid.Parse("3f8a1b2c-4d5e-4f7a-8b9c-0d1e2f3a4b5c");
    private static readonly Guid W2Uid  = Guid.Parse("4a9b2c3d-5e6f-4a7b-9c0d-1e2f3a4b5c6d");
    private static readonly Guid S1Uid  = Guid.Parse("b1c2d3e4-f5a6-4b7c-8d9e-0f1a2b3c4d5e");
    private static readonly Guid S11Uid = Guid.Parse("a7b8c9d0-e1f2-4a3b-5c6d-7e8f9a0b1c2d");

    // -------------------------------------------------------------------------
    // Hilfsmethode + optionaler Einsprungpunkt
    // -------------------------------------------------------------------------

    /// <summary>Runs all examples in sequence using the provided command station.</summary>
    public static async Task RunAllExamplesAsync(ICommandStation commandStation)
    {
        //await Example1_LoadAndInspect(commandStation);
        //await Example2_TurnoutStateControl(commandStation);
        //await Example3_SignalWithEvents(commandStation);
        await Example4_MultiDecoderSubscribe(commandStation);
        //await Example5_ErrorHandling(commandStation);
        //await Example6_LiveLoDiAccessoryEventMonitor(commandStation);
        Console.WriteLine("=== Alle Beispiele abgeschlossen ===");
    }

    // -------------------------------------------------------------------------
    // Example 1 – Konfiguration laden und inspizieren
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads an accessory, subscribes a command station and prints configuration details.
    /// </summary>
    public static async Task Example1_LoadAndInspect(ICommandStation station)
    {
        Console.WriteLine("=== Example 1: Load, Subscribe And Inspect ===\n");

        // 1. Accessory ohne Zentrale erstellen (nur Konfiguration parsen)
        var accessory = new AccessoryItem(W1Uid);

        Console.WriteLine($"Accessory: {accessory.Type}/{accessory.Subtype} {accessory.Id}");
        Console.WriteLine($"Interlocking: {accessory.Interlocking}");
        Console.WriteLine($"Protocol: {accessory.Protocol}");
        Console.WriteLine($"Zustände: {string.Join(", ", accessory.GetAvailableStateIds())}");
        Console.WriteLine($"AccessoryDecoder-Instanzen: {accessory.Decoders.Count}");
        var subscribedStationName = accessory.SubscribedCommandStation?.GetType().Name ?? "<keine>";
        Console.WriteLine($"Abonnierte Zentrale: {subscribedStationName}");

        // 2. Zentrale abonnieren
        await accessory.SubscribeCommandStationAsync(station);
        Console.WriteLine($"Abonnierte Zentrale nach Subscribe: {accessory.SubscribedCommandStation?.GetType().Name}");

        // 3. Zustand setzen
        await accessory.SetStateAsync("straight");
        Console.WriteLine($"CurrentState: {accessory.CurrentState}\n");

        // 4. Abmelden
        await accessory.UnsubscribeCommandStationAsync(station);
        Console.WriteLine($"Abonnierte Zentrale nach Unsubscribe: {(accessory.SubscribedCommandStation is null ? "<keine>" : accessory.SubscribedCommandStation.GetType().Name)}\n");
    }

    // -------------------------------------------------------------------------
    // Example 2 – Zustandswechsel einer einfachen Weiche
    // -------------------------------------------------------------------------

    /// <summary>
    /// Switches W1 through all configured states.
    /// </summary>
    public static async Task Example2_TurnoutStateControl(ICommandStation station)
    {
        Console.WriteLine("=== Example 2: Turnout State Control ===\n");

        var turnout = new AccessoryItem(W1Uid);
        await turnout.SubscribeCommandStationAsync(station);

        foreach (var stateId in turnout.GetAvailableStateIds())
        {
            await turnout.SetStateAsync(stateId);
            Console.WriteLine($"  W1 CurrentState: {turnout.CurrentState}");
            await Task.Delay(2000); // Kurze Pause zwischen den Zustandswechseln
        }

        Console.WriteLine();
    }

    // -------------------------------------------------------------------------
    // Example 3 – Signal mit Event-Weiterleitung von Kind-Decodern
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows how state-change events from child decoders bubble up to the accessory.
    /// </summary>
    public static async Task Example3_SignalWithEvents(ICommandStation station)
    {
        Console.WriteLine("=== Example 3: Signal With Events ===\n");

        var signal = new AccessoryItem(S1Uid);
        signal.StateChanged += (_, args) =>
            Console.WriteLine($"  [Event] AccessoryDecoder {args.Address}, OutputValue {args.OutputValue} => " +
                              $"{args.State}");

        await signal.SubscribeCommandStationAsync(station);

        await signal.SetStateAsync("Halt");
        await signal.SetStateAsync("Im1");
        await signal.SetStateAsync("Im2");
        Console.WriteLine($"  CurrentState: {signal.CurrentState}\n");
    }

    // -------------------------------------------------------------------------
    // Example 4 – Mehrdecoder-Zubehör (DKW W2: Adressen 11 und 12)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Demonstrates subscribe across multiple child decoders for one accessory.
    /// W2 (Doppelkreuzungsweiche) uses DCC addresses 11 and 12.
    /// </summary>
    public static async Task Example4_MultiDecoderSubscribe(ICommandStation station)
    {
        Console.WriteLine("=== Example 4: Multi-AccessoryDecoder Subscribe ===\n");

        var dkw = new AccessoryItem(W2Uid);
        Console.WriteLine($"AccessoryDecoder-Instanzen vor Subscribe: {dkw.Decoders.Count}");
        Console.WriteLine("AccessoryDecoder-Adressen: " +
                          string.Join(", ", dkw.Decoders.Select(d => d.Address.ToString())));

        // Eine Subscribe-Anfrage subscribed automatisch alle Kind-AccessoryDecoder
        await dkw.SubscribeCommandStationAsync(station);
        Console.WriteLine("Alle AccessoryDecoder an eine Zentrale gebunden: " +
                          string.Join(", ", dkw.Decoders.Select(d =>
                              $"Addr {d.Address} → {(d.SubscribedCommandStation is null ? "<keine>" : d.SubscribedCommandStation.GetType().Name)}")));

        foreach (var stateId in dkw.GetAvailableStateIds())
        {
            await dkw.SetStateAsync(stateId);
            Console.WriteLine($"  W2 CurrentState: {dkw.CurrentState}");
            Console.WriteLine("  -> Beliebige Taste fuer den naechsten Zustand druecken...");
            await Task.Run(() => Console.ReadKey(intercept: true));
        }

        // Unsubscribe trifft alle Kind-AccessoryDecoder
        await dkw.UnsubscribeCommandStationAsync(station);
        Console.WriteLine("Unsubscribe erfolgt. Zentrale je AccessoryDecoder: " +
                          string.Join(", ", dkw.Decoders.Select(d =>
                              $"Addr {d.Address} → {(d.SubscribedCommandStation is null ? "<keine>" : d.SubscribedCommandStation.GetType().Name)}")));
        Console.WriteLine();
    }

    // -------------------------------------------------------------------------
    // Example 5 – Fehlerbehandlung
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows error behaviour for unknown/empty state ids and missing subscription.
    /// </summary>
    public static async Task Example5_ErrorHandling(ICommandStation station)
    {
        Console.WriteLine("=== Example 5: Error Handling ===\n");

        var signal = new AccessoryItem(S11Uid);
        await signal.SubscribeCommandStationAsync(station);

        // Unbekannter Zustand
        try
        {
            await signal.SetStateAsync("UNBEKANNT");
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.WriteLine($"  Erwartet: {ex.ParamName} – {ex.Message.Split('\n')[0]}");
        }

        // Leerer Zustand
        try
        {
            await signal.SetStateAsync(" ");
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"  Erwartet: {ex.Message}");
        }

        // Kein Befehl ohne abonnierte Zentrale (nur Log, kein Wurf)
        var noStation = new AccessoryItem(W1Uid);
        await noStation.SetStateAsync("straight");   // wird ignoriert, kein Fehler
        Console.WriteLine($"  Ohne Zentrale: SetState lief durch ohne Wurf (CurrentState={noStation.CurrentState})\n");
    }

    // -------------------------------------------------------------------------
    // Example 6 - Laufende Ueberwachung von LoDi- und Accessory-Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Live readback test for accessories, analogous to ReadBackTest_Loco:
    /// - creates a concrete accessory instance first (W1)
    /// - subscribes command-station accessory readback events
    /// - subscribes per-decoder readback events of that accessory
    /// - runs until key press or cancellation
    /// </summary>
    public static async Task Example6_LiveLoDiAccessoryEventMonitor(
        ICommandStation station,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("=== Example 6: Live LoDi Accessory Event Monitor ===\n");
        // Feste Test-Weiche fuer reproduzierbares Readback-Verhalten.
        var accessory = new AccessoryItem(W1Uid);
        var states = accessory.GetAvailableStateIds();
        var monitoredAddresses = accessory.Decoders
            .Select(decoder => decoder.Address)
            .ToHashSet();

        var commandStationEventCount = 0;
        EventHandler<AccessoryStateChangedEventArgs> stationHandler = (_, args) =>
        {
            var eventNumber = Interlocked.Increment(ref commandStationEventCount);
            var threadId = Thread.CurrentThread.ManagedThreadId;
            var scope = monitoredAddresses.Contains(args.Address) ? "MATCH" : "FREMD";
            Console.WriteLine(
                $"[CS Accessory ReadBack #{eventNumber}] Thread={threadId} Scope={scope} " +
                $"Addr={args.Address} OutputValue={args.OutputValue} State={args.State}");
        };

        EventHandler<AccessoryStateChangedEventArgs> accessoryHandler = (_, args) =>
            Console.WriteLine(
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] [Accessory/StateChanged] " +
                $"Addr={args.Address} OutputValue={args.OutputValue} State={args.State}");

        station.AccessoryStateChanged += stationHandler;
        accessory.StateChanged += accessoryHandler;

        await accessory.SubscribeCommandStationAsync(station, cancellationToken).ConfigureAwait(false);

        Console.WriteLine("=== Accessory ReadBack Live-Test ===");
        Console.WriteLine($"Accessory: {accessory.Type}/{accessory.Subtype} {accessory.Id}");
        Console.WriteLine($"UID: {accessory.AccessoryId}");
        Console.WriteLine($"Zustaende (Info): {(states.Count == 0 ? "<keine>" : string.Join(", ", states))}");
        Console.WriteLine($"Beobachtete Decoder-Adressen: {string.Join(", ", monitoredAddresses.OrderBy(x => x))}");
        Console.WriteLine("Abonniere StateChanged-Events auf allen Accessory-Decodern...");

        var subscribedDecoders = 0;
        foreach (var decoder in accessory.Decoders)
        {
            ShowAccessoryStateChangedResultsContinuously(decoder);
            subscribedDecoders++;
        }

        Console.WriteLine("Passiver Modus: Es werden keine SetState-Aufrufe aus diesem Beispiel gesendet.");
        Console.WriteLine($"Abonnierte Decoder: {subscribedDecoders}");
        Console.WriteLine("Warte auf Zustandsaenderungen von der Zentrale...");
        Console.WriteLine("(Steuerung extern bedienen, z.B. Weiche schalten)");
        Console.WriteLine("-> Beliebige Taste druecken zum Beenden (oder CancellationToken).\n");

        try
        {
            var keyTask = Task.Run(() => Console.ReadKey(intercept: true));
            var cancelTask = Task.Delay(Timeout.Infinite, cancellationToken);
            await Task.WhenAny(keyTask, cancelTask).ConfigureAwait(false);
            Console.WriteLine("=== Accessory ReadBack Live-Test beendet ===");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Accessory ReadBack Live-Test gestoppt (Cancellation).\n");
        }
        finally
        {
            accessory.StateChanged -= accessoryHandler;
            station.AccessoryStateChanged -= stationHandler;
            await accessory.UnsubscribeCommandStationAsync(station).ConfigureAwait(false);
        }
    }

    private static void ShowAccessoryStateChangedResultsContinuously(OTD.HardwareControl.Accessory.IAccessoryDecoder accessoryDecoder)
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
    // Optional standalone runner:
    // public static async Task Main()
    // {
    //     // Provide a real or mocked ICommandStation here.
    // }
    //
    // Example usage:
    // await AccessoryDecoderIntegrationExample.RunAllExamplesAsync(commandStation);
}
