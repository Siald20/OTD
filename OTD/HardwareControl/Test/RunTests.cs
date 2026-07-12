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
using System.Threading.Tasks;
using System.Xml.Linq;
using OTD.HardwareControl.Drivers;
using OTD.HardwareControl.Test;
using OTD.TrainDriving.Examples;
using OTD.TrainDriving.RouteControl.Services;
using OTD.TrainDriving.RouteControl.Tests;

namespace OTD.HardwareControl.Test;

internal static class RunTests
{
    public static void Run()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private static async Task RunAsync()
    {
        var directTestId = Environment.GetEnvironmentVariable("OTD_TEST_CASE")?.Trim();
        if (string.Equals(directTestId, "TRAINDRIVING_ROUTE_LAYOUT_MINIMAL", StringComparison.OrdinalIgnoreCase))
        {
            RouteLayoutMinimalExample.Run();
            return;
        }

        if (string.Equals(directTestId, "TRAINDRIVING_ROUTELEG_BUILD_TRACE", StringComparison.OrdinalIgnoreCase))
        {
            RouteLegBuildTrace.RunInteractive();
            return;
        }

        if (string.Equals(directTestId, "TRAINDRIVING_ROUTELEG_DIRECTION_TEST", StringComparison.OrdinalIgnoreCase))
        {
            RouteLegDirectionTest.RunAll();
            return;
        }

        if (string.Equals(directTestId, "TRAINDRIVING_STUCK_ALERT_TEST", StringComparison.OrdinalIgnoreCase))
        {
            StuckAlertEmergencyStopTest.RunAll();
            return;
        }

        if (string.Equals(directTestId, "TRAINDRIVING_UNEXPECTED_AHEAD_SENSOR_TEST", StringComparison.OrdinalIgnoreCase))
        {
            UnexpectedAheadFeedbackInputEmergencyStopTest.RunAll();
            return;
        }

        if (string.Equals(directTestId, "TRAINDRIVING_LAYOUT_LEGS_DUMP", StringComparison.OrdinalIgnoreCase))
        {
            var layoutPath = XmlRailwayLayoutService.GetDefaultConfigFilePath();
            var topologyPath = XmlRailwayLayoutService.GetDefaultTopologyFilePath();
            var layoutService = new XmlRailwayLayoutService(layoutPath, topologyPath);
            var legs = layoutService.GetAllGeneratedLegs()
                .OrderBy(leg => leg.FromWaypointId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(leg => leg.ToWaypointId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Console.WriteLine($"Layout: {layoutPath}");
            Console.WriteLine($"Generated legs: {legs.Count}");
            Console.WriteLine();
            foreach (var leg in legs)
            {
                var markers = leg.FeedbackActivationPoints?.Count ?? 0;
                Console.WriteLine($"  {leg.FromWaypointId,-15} -> {leg.ToWaypointId,-15}  dist={leg.DistanceCm,4} cm  sensors={markers}");
            }
            return;
        }

        var configPath = CommandStationUtils.GetDefaultConfigFilePath();
        var document = CommandStationUtils.LoadXDocument(configPath);

        var commandStations = LoadCommandStations(document);
        var feedbackStations = LoadFeedbackStations(document);

        if (commandStations.Count == 0)
            throw new InvalidOperationException("No commandstations configured in commandstations.xml.");

        if (feedbackStations.Count == 0)
            throw new InvalidOperationException(
                "No feedback-capable commandstations configured in commandstations.xml.");

        Console.Clear();
        Console.WriteLine("=== RunTests ===");
        Console.WriteLine($"Config: {configPath}");
        Console.WriteLine();

        var selectedCommandStation = SelectOption("Verfuegbare Commandstations", commandStations);
        Console.WriteLine();
        var selectedFeedback = SelectOption("Verfuegbare Feedback-Commandstations", feedbackStations);

        Console.WriteLine();
        Console.WriteLine("Auswahl abgeschlossen:");
        Console.WriteLine($"  CommandStation: {selectedCommandStation.Display}");
        Console.WriteLine($"  Feedback:       {selectedFeedback.Display}");
        Console.WriteLine();

        var commandStationUid = ParseGuidOrThrow(selectedCommandStation.Id, "commandstation uid");
        var feedbackUid = ParseGuidOrThrow(selectedFeedback.Id, "feedback commandstation uid");

        using var commandStation = new CommandStation(commandStationUid);
        using var feedbackModule = new FeedbackController(feedbackUid);

        
        
        var testEntries = BuildTestEntries(commandStationUid, feedbackUid);

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("Waehle Test (Esc zum Beenden):");

            SelectableEntry selectedTest;
            try
            {
                selectedTest = SelectOption("Verfuegbare Tests", testEntries.Select(entry => entry.MenuEntry).ToList());
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Testauswahl beendet.");
                return;
            }

            var test = testEntries.First(entry => entry.MenuEntry.Id == selectedTest.Id);

            try
            {
                if (test.RequiresCommandStation)
                    await EnsureCommandStationReadyAsync(commandStation).ConfigureAwait(false);

                if (test.RequiresFeedback)
                    await EnsureFeedbackReadyAsync(feedbackModule).ConfigureAwait(false);

                Console.WriteLine();
                Console.WriteLine($"Starte Test: {test.MenuEntry.Display}");
                await test.ExecuteAsync(commandStation, feedbackModule).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Testfehler: {ex.Message}, {ex.StackTrace}");
            }

            Console.WriteLine();
            Console.WriteLine("Test beendet. Taste druecken fuer Rueckkehr zum Menue...");
            Console.ReadKey(true);
        }
    }

    private static List<TestEntry> BuildTestEntries(Guid selectedStationUid, Guid selectedFeedbackUid)
    {
        return
        [
            new TestEntry(
                new SelectableEntry("TRAINDRIVING_RoundTripBi_BiDi", "test", "TrainDriving: RoundTripBi_BiDi (Zug + Richtung waehlen)", null, 0),
                (cs, fb) =>
                {
                    TestRoundTripBi_BiDi.Run(cs, fb);
                    return Task.CompletedTask;
                },
                true,
                true),
            
            new TestEntry(
                new SelectableEntry("TRAINDRIVING_BRAKE_SENSOR56", "test", "TrainDriving: Sensor56 -> Bremsen 40->0 auf 200cm", null, 0),
                (cs, fb) =>
                {
                    TrayectoryTestSingleFeedback.Run(cs, fb);
                    return Task.CompletedTask;
                },
                true,
                true),

            new TestEntry(
                new SelectableEntry("OPERATION_FLOW_BI", "test", "OperationFlowBi (Ablauf mit Sensorlogik)", null, 0),
                (cs, fb) =>
                {
                    TestOperationFlowBi.Run(cs, fb);
                    return Task.CompletedTask;
                },
                true,
                true),

            new TestEntry(
                new SelectableEntry("FEEDBACK_SINGLE", "test", "FeedbackController Einzelmodul", null, 0),
                (_, _) => FeedbackTests.RunSingleModuleAsync(selectedFeedbackUid),
                true,
                true),

            new TestEntry(
                new SelectableEntry("READBACK_LOCO", "test", "Readback Lokdecoder", null, 0),
                (cs, _) => ReadbackTests.ReadBackTestLocoAsync(cs),
                true,
                false),

            new TestEntry(
                new SelectableEntry("READBACK_ACCESSORY", "test", "Readback Zubehoerdecoder", null, 0),
                (cs, _) => ReadbackTests.ReadBackTestAccessoryAsync(cs),
                true,
                false),

            new TestEntry(
                new SelectableEntry("FEEDBACK_TRIGGER_DIAG", "test", "FeedbackController Trigger Diagnose", null, 0),
                (_, _) =>
                {
                    var observeSeconds = PromptInt("Beobachtungsfenster in Sekunden", 30, 5, 600);
                    var pulseAddress = PromptInt("Pulse-Adresse", 4, 1, 2048);
                    var pulseValue = PromptByte("Pulse-Wert", 1);
                    return FeedbackTests.RunEnsureOperationalTriggerTestAsync(
                        selectedStationUid,
                        TimeSpan.FromSeconds(observeSeconds),
                        pulseAddress,
                        pulseValue);
                },
                false,
                false),

            new TestEntry(
                new SelectableEntry("FEEDBACK_MOCK_KEYBOARD", "test", "Mock Keyboard Feedback", null, 0),
                (_, _) => MockKeyboardFeedback.RunAsync(),
                false,
                false)
            ,
            new TestEntry(
                new SelectableEntry("TRAINDRIVING_ROUTE_LAYOUT_MINIMAL", "test", "TrainDriving: Minimales topologiebasiertes Layout-Beispiel (ohne Hardware)", null, 0),
                (_, _) =>
                {
                    RailwayLayoutDirectedTable.Run();
                    //RouteLayoutMinimalExample.Run();
                    return Task.CompletedTask;
                },
                false,
                false)
            ,
            new TestEntry(
                new SelectableEntry("TRAINDRIVING_ROUTELEG_BUILD_TRACE", "test", "TrainDriving: RouteLeg-Aufbau Trace (Segmente/FeedbackActivationPoint)", null, 0),
                (_, _) =>
                {
                    RouteLegBuildTrace.RunInteractive();
                    return Task.CompletedTask;
                },
                false,
                false)
            ,
            new TestEntry(
                new SelectableEntry("TRAINDRIVING_ROUTELEG_DIRECTION_TEST", "test", "TrainDriving: RouteLeg Richtungstest AlongLine/AgainstLine (ohne Hardware)", null, 0),
                (_, _) =>
                {
                    RouteLegDirectionTest.RunAll();
                    return Task.CompletedTask;
                },
                false,
                false)
            ,
            new TestEntry(
                new SelectableEntry("TRAINDRIVING_STUCK_ALERT_TEST", "test", "TrainDriving: StuckAlert Notbrems-Guard (prozentbasiert, ohne Hardware)", null, 0),
                (_, _) =>
                {
                    StuckAlertEmergencyStopTest.RunAll();
                    return Task.CompletedTask;
                },
                false,
                false)
            ,
            new TestEntry(
                new SelectableEntry("TRAINDRIVING_UNEXPECTED_AHEAD_SENSOR_TEST", "test", "TrainDriving: Unexpected-Ahead-Sensor Notbrems-Guard (ohne Hardware)", null, 0),
                (_, _) =>
                {
                    UnexpectedAheadFeedbackInputEmergencyStopTest.RunAll();
                    return Task.CompletedTask;
                },
                false,
                false)
        ];
    }

    private static async Task EnsureCommandStationReadyAsync(CommandStation commandStation)
    {
        if (!commandStation.IsConnected)
            await commandStation.ConnectAsync().ConfigureAwait(false);

        await commandStation.SetPowerAsync(true).ConfigureAwait(false);
        var powerState = await commandStation.GetPowerStateAsync().ConfigureAwait(false);
        Console.WriteLine($"  CommandStation bereit, Gleisspannung: {(powerState ? "EIN" : "AUS")}");
    }

    private static async Task EnsureFeedbackReadyAsync(FeedbackController feedbackModule)
    {
        if (!feedbackModule.IsConnected)
            await feedbackModule.ConnectAsync().ConfigureAwait(false);

        var ready = await feedbackModule.EnsureOperationalAsync().ConfigureAwait(false);
        if (!ready)
            throw new InvalidOperationException(
                "FeedbackController konnte nicht in einen betriebsbereiten Zustand gebracht werden.");

        Console.WriteLine($"  FeedbackController bereit, Sensoren: {feedbackModule.InputCount}");
    }

    private static int PromptInt(string label, int defaultValue, int min, int max)
    {
        while (true)
        {
            Console.Write($"{label} [{defaultValue}]: ");
            var raw = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(raw))
                return defaultValue;

            if (int.TryParse(raw, out var parsed) && parsed >= min && parsed <= max)
                return parsed;

            Console.WriteLine($"Ungueltige Eingabe. Erlaubt: {min}..{max}");
        }
    }

    private static byte PromptByte(string label, byte defaultValue)
    {
        while (true)
        {
            Console.Write($"{label} [{defaultValue}]: ");
            var raw = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(raw))
                return defaultValue;

            if (byte.TryParse(raw, out var parsed))
                return parsed;

            Console.WriteLine("Ungueltige Eingabe. Erlaubt: 0..255");
        }
    }

    private static List<SelectableEntry> LoadCommandStations(XDocument document)
    {
        return document.Root?
                   .Elements("commandstation")
                   .Where(element => !IsFeedbackDriver(element.Attribute("driver")?.Value))
                   .Select(element =>
                   {
                       var uid = element.Attribute("uid")?.Value ?? "<missing-uid>";
                       var driver = element.Attribute("driver")?.Value ?? "<missing-driver>";
                       var ip = element.Element("connection")?.Attribute("ip")?.Value;
                       var portRaw = element.Element("connection")?.Attribute("port")?.Value;
                       var port = int.TryParse(portRaw, out var parsedPort) ? parsedPort : 0;
                       var endpoint = string.IsNullOrWhiteSpace(ip) ? "-" : $"{ip}:{port}";
                       return new SelectableEntry(uid, driver, $"UID={uid} | Driver={driver} | Endpoint={endpoint}", ip,
                           port);
                   })
                   .ToList()
               ?? [];
    }

    private static List<SelectableEntry> LoadFeedbackStations(XDocument document)
    {
        return document.Root?
                   .Elements("commandstation")
                   .Where(element => IsFeedbackDriver(element.Attribute("driver")?.Value))
                   .Select(element =>
                   {
                       var uid = element.Attribute("uid")?.Value ?? "<missing-uid>";
                       var driver = element.Attribute("driver")?.Value ?? "<missing-driver>";
                       var ip = element.Element("connection")?.Attribute("ip")?.Value;
                       var portRaw = element.Element("connection")?.Attribute("port")?.Value;
                       var port = int.TryParse(portRaw, out var parsedPort) ? parsedPort : 0;
                       var endpoint = string.IsNullOrWhiteSpace(ip) ? "-" : $"{ip}:{port}";
                       return new SelectableEntry(uid, driver, $"UID={uid} | Driver={driver} | Endpoint={endpoint}", ip,
                           port);
                   })
                   .ToList()
               ?? [];
    }

    private static bool IsFeedbackDriver(string? driver)
    {
        return string.Equals(driver, "lodi-s88-commander", StringComparison.OrdinalIgnoreCase)
               || string.Equals(driver, "mock-keyboard-feedback", StringComparison.OrdinalIgnoreCase);
    }

    private static SelectableEntry SelectOption(string title, IReadOnlyList<SelectableEntry> options)
    {
        var index = 0;

        while (true)
        {
            RenderMenu(title, options, index);
            var key = Console.ReadKey(true).Key;

            switch (key)
            {
                case ConsoleKey.UpArrow:
                    index = index == 0 ? options.Count - 1 : index - 1;
                    break;
                case ConsoleKey.DownArrow:
                    index = (index + 1) % options.Count;
                    break;
                case ConsoleKey.Enter:
                    Console.WriteLine($"Ausgewaehlt: {options[index].Display}");
                    return options[index];
                case ConsoleKey.Escape:
                    throw new OperationCanceledException("Selection aborted by user.");
            }
        }
    }

    private static void RenderMenu(string title, IReadOnlyList<SelectableEntry> options, int selectedIndex)
    {
        Console.Clear();
        Console.WriteLine("=== RunTests ===");
        Console.WriteLine();
        Console.WriteLine(title);
        Console.WriteLine("(Pfeil hoch/runter, Enter = waehlen, Esc = abbrechen)");
        Console.WriteLine();

        for (var i = 0; i < options.Count; i++)
        {
            var isSelected = i == selectedIndex;
            var label = isSelected
                ? $"=> [ {options[i].Display} ]"
                : $"   {options[i].Display}";


            Console.WriteLine(label);
        }
    }

    private static Guid ParseGuidOrThrow(string raw, string context)
    {
        if (Guid.TryParse(raw, out var value))
            return value;

        throw new InvalidOperationException($"Invalid {context}: '{raw}'");
    }

    private sealed record TestEntry(
        SelectableEntry MenuEntry,
        Func<CommandStation, FeedbackController, Task> ExecuteAsync,
        bool RequiresCommandStation,
        bool RequiresFeedback);

    private sealed record SelectableEntry(string Id, string Driver, string Display, string? IpAddress, int Port);
}