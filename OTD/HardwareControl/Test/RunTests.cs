// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - DecoderControl
// Copyright (C) 2026

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace OTD.HardwareControl;

internal static class RunTests
{
    public static void Run()
    {
        var configPath = CommandStationUtils.GetDefaultConfigFilePath();
        var document = CommandStationUtils.LoadXDocument(configPath);

        var commandStations = LoadCommandStations(document);
        var feedbackModules = LoadFeedbackModules(document);

        if (commandStations.Count == 0)
            throw new InvalidOperationException("No commandstations configured in deviceconfig.xml.");

        if (feedbackModules.Count == 0)
            throw new InvalidOperationException("No feedbackmodules configured in deviceconfig.xml.");

        Console.Clear();
        Console.WriteLine("=== RunTests ===");
        Console.WriteLine($"Config: {configPath}");
        Console.WriteLine();

        var selectedCommandStation = SelectOption("Verfuegbare Commandstations", commandStations);
        Console.WriteLine();
        var selectedFeedback = SelectOption("Verfuegbare Feedbacks", feedbackModules);

        Console.WriteLine();
        Console.WriteLine("Auswahl abgeschlossen:");
        Console.WriteLine($"  CommandStation: {selectedCommandStation.Display}");
        Console.WriteLine($"  Feedback:       {selectedFeedback.Display}");
        Console.WriteLine();

        var commandStationUid = ParseGuidOrThrow(selectedCommandStation.Id, "commandstation uid");
        var feedbackUid = ParseGuidOrThrow(selectedFeedback.Id, "feedbackmodule uid");

        using var commandStation = new CommandStation(commandStationUid);
        using var feedbackModule = new Feedback(feedbackUid);

        Console.WriteLine("Instanzen erstellt:");
        Console.WriteLine($"  CommandStation.UniqueId:   {commandStation.UniqueId}");
        Console.WriteLine($"  CommandStation.DriverName: {commandStation.DriverName}");
        Console.WriteLine($"  Feedback.UniqueId:         {feedbackModule.UniqueId}");
        Console.WriteLine($"  Feedback.DriverName:       {feedbackModule.DriverName}");
        Console.WriteLine();

        Console.WriteLine("Initialisiere CommandStation...");
        commandStation.ConnectAsync().GetAwaiter().GetResult();
        Console.WriteLine($"  Verbunden: {commandStation.IsConnected}");

        Console.WriteLine("Initialisiere Feedback...");
        feedbackModule.ConnectAsync().GetAwaiter().GetResult();
        var feedbackReady = feedbackModule.EnsureOperationalAsync().GetAwaiter().GetResult();
        Console.WriteLine($"  Verbunden: {feedbackModule.IsConnected}");
        Console.WriteLine($"  Operational: {feedbackReady}");

        Console.WriteLine("Schalte Gleisspannung ein...");
        commandStation.SetPowerAsync(true).GetAwaiter().GetResult();
        var powerState = commandStation.GetPowerStateAsync().GetAwaiter().GetResult();
        Console.WriteLine($"  Gleisspannung: {(powerState ? "EIN" : "AUS")}");

        Console.WriteLine();
        TestOperationFlowBi.Run(commandStation, feedbackModule);
    }

    private static List<SelectableEntry> LoadCommandStations(XDocument document)
    {
        return document.Root?
            .Element("commandstations")?
            .Elements("commandstation")
            .Select(element =>
            {
                var uid = element.Attribute("uid")?.Value ?? "<missing-uid>";
                var driver = element.Attribute("driver")?.Value ?? "<missing-driver>";
                var ip = element.Element("connection")?.Attribute("ip")?.Value;
                var portRaw = element.Element("connection")?.Attribute("port")?.Value;
                var port = int.TryParse(portRaw, out var parsedPort) ? parsedPort : 0;
                var endpoint = string.IsNullOrWhiteSpace(ip) ? "-" : $"{ip}:{port}";
                return new SelectableEntry(uid, driver, $"UID={uid} | Driver={driver} | Endpoint={endpoint}", ip, port);
            })
            .ToList()
            ?? [];
    }

    private static List<SelectableEntry> LoadFeedbackModules(XDocument document)
    {
        return document.Root?
            .Element("feedbackmodules")?
            .Elements("feedbackmodule")
            .Select(element =>
            {
                var uid = element.Attribute("uid")?.Value ?? "<missing-uid>";
                var driver = element.Attribute("driver")?.Value ?? "<missing-driver>";
                var ip = element.Element("connection")?.Attribute("ip")?.Value;
                var portRaw = element.Element("connection")?.Attribute("port")?.Value;
                var port = int.TryParse(portRaw, out var parsedPort) ? parsedPort : 0;
                var endpoint = string.IsNullOrWhiteSpace(ip) ? "-" : $"{ip}:{port}";
                return new SelectableEntry(uid, driver, $"UID={uid} | Driver={driver} | Endpoint={endpoint}", ip, port);
            })
            .ToList()
            ?? [];
    }

    private static SelectableEntry SelectOption(string title, IReadOnlyList<SelectableEntry> options)
    {
        var index = 0;

        while (true)
        {
            RenderMenu(title, options, index);
            var key = Console.ReadKey(intercept: true).Key;

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

    private sealed record SelectableEntry(string Id, string Driver, string Display, string? IpAddress, int Port);
}