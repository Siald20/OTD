using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OTD.TrackPlan;

public sealed class RouteDocumentStore
{
    public IReadOnlyList<RouteResult> Load(string filePath, TrackPlanGraph graph)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        var xml = XDocument.Load(filePath);
        var routes = new List<RouteResult>();

        foreach (var routeElement in xml.Root?.Elements("Route") ?? [])
        {
            var startSignalId = ReadString(routeElement, "startSignalId", string.Empty);
            var targetSignalId = ReadString(routeElement, "targetSignalId", string.Empty);
            if (!graph.TryGetSymbol(startSignalId, out var startSignal) ||
                startSignal is null ||
                !graph.TryGetSymbol(targetSignalId, out var targetSignal) ||
                targetSignal is null)
            {
                continue;
            }

            var connections = routeElement.Element("Connections")?.Elements("Connection")
                .Select(static element => new TrackConnection
                {
                    FromSymbolId = ReadString(element, "fromSymbolId", string.Empty),
                    FromPort = ReadString(element, "fromPort", string.Empty),
                    ToSymbolId = ReadString(element, "toSymbolId", string.Empty),
                    ToPort = ReadString(element, "toPort", string.Empty),
                    Cost = ReadInt(element, "cost", 1),
                    IsEnabled = ReadBool(element, "isEnabled", true)
                })
                .Where(connection =>
                    !string.IsNullOrWhiteSpace(connection.FromSymbolId) &&
                    !string.IsNullOrWhiteSpace(connection.ToSymbolId))
                .ToList() ?? [];

            var symbolIds = routeElement.Element("Symbols")?.Elements("Symbol")
                .Select(static element => ReadString(element, "id", string.Empty))
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .ToList() ?? [];

            var symbols = symbolIds
                .Select(id => graph.TryGetSymbol(id, out var symbol) ? symbol : null)
                .Where(static symbol => symbol is not null)
                .Cast<TrackSymbol>()
                .ToList();

            if (symbols.Count == 0)
            {
                symbols.Add(startSignal);
                symbols.Add(targetSignal);
            }

            var switchCommands = routeElement.Element("SwitchCommands")?.Elements("SwitchCommand")
                .Select(static element => new SwitchCommand
                {
                    SwitchId = ReadString(element, "switchId", string.Empty),
                    SwitchName = ReadString(element, "switchName", string.Empty),
                    Position = ReadEnum(element, "position", SwitchPosition.Straight)
                })
                .Where(static command => !string.IsNullOrWhiteSpace(command.SwitchId))
                .ToList() ?? [];

            routes.Add(new RouteResult
            {
                StartSignal = startSignal,
                TargetSignal = targetSignal,
                Symbols = symbols,
                Connections = connections,
                SwitchCommands = switchCommands,
                Cost = ReadInt(routeElement, "cost", 0)
            });
        }

        return routes;
    }

    public void Save(string filePath, IReadOnlyList<RouteResult> routes)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var xml = new XDocument(
            new XElement("Routes",
                routes.Select(route =>
                    new XElement("Route",
                        new XAttribute("startSignalId", route.StartSignal.Id),
                        new XAttribute("targetSignalId", route.TargetSignal.Id),
                        new XAttribute("cost", route.Cost.ToString(CultureInfo.InvariantCulture)),
                        new XElement("Symbols",
                            route.Symbols.Select(symbol =>
                                new XElement("Symbol",
                                    new XAttribute("id", symbol.Id)))),
                        new XElement("Connections",
                            route.Connections.Select(connection =>
                                new XElement("Connection",
                                    new XAttribute("fromSymbolId", connection.FromSymbolId),
                                    new XAttribute("fromPort", connection.FromPort),
                                    new XAttribute("toSymbolId", connection.ToSymbolId),
                                    new XAttribute("toPort", connection.ToPort),
                                    new XAttribute("cost", connection.Cost.ToString(CultureInfo.InvariantCulture)),
                                    new XAttribute("isEnabled", connection.IsEnabled.ToString())))),
                        new XElement("SwitchCommands",
                            route.SwitchCommands.Select(command =>
                                new XElement("SwitchCommand",
                                    new XAttribute("switchId", command.SwitchId),
                                    new XAttribute("switchName", command.SwitchName),
                                    new XAttribute("position", command.Position.ToString()))))))));

        xml.Save(filePath);
    }

    private static string ReadString(XElement element, string attributeName, string fallback)
    {
        return (string?)element.Attribute(attributeName) ?? fallback;
    }

    private static int ReadInt(XElement element, string attributeName, int fallback)
    {
        return int.TryParse((string?)element.Attribute(attributeName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static bool ReadBool(XElement element, string attributeName, bool fallback)
    {
        return bool.TryParse((string?)element.Attribute(attributeName), out var value)
            ? value
            : fallback;
    }

    private static T ReadEnum<T>(XElement element, string attributeName, T fallback)
        where T : struct
    {
        return Enum.TryParse<T>((string?)element.Attribute(attributeName), out var value)
            ? value
            : fallback;
    }
}
