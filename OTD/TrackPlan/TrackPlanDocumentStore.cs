using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace OTD.TrackPlan;

public sealed class TrackPlanDocumentStore
{
    public TrackPlanDocument Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new TrackPlanDocument();
        }

        var xml = XDocument.Load(filePath);
        var root = xml.Root;
        if (root is null)
        {
            return new TrackPlanDocument();
        }

        var document = new TrackPlanDocument
        {
            Name = ReadString(root, "name", "Neuer Gleisplan"),
            Version = ReadInt(root, "version", 1)
        };

        foreach (var element in root.Element("Symbols")?.Elements("Symbol") ?? [])
        {
            var kind = ReadEnum(element, "kind", TrackSymbolKind.Track);
            if (kind is TrackSymbolKind.TrackBlock)
            {
                kind = TrackSymbolKind.LineBlock;
            }

            var symbol = new DrawnTrackSymbol
            {
                Id = ReadString(element, "id", string.Empty),
                Name = ReadString(element, "name", string.Empty),
                Kind = kind,
                X = ReadInt(element, "x", 0),
                Y = ReadInt(element, "y", 0),
                SignalDirection = ReadEnum(element, "signalDirection", SignalDirection.Both),
                CurrentSwitchPosition = ReadEnum(element, "currentSwitchPosition", SwitchPosition.Straight)
            };

            foreach (var option in element.Element("SwitchRouteOptions")?.Elements("Option") ?? [])
            {
                symbol.SwitchRouteOptions.Add(new SwitchRouteOption
                {
                    FromPort = ReadString(option, "fromPort", string.Empty),
                    ToPort = ReadString(option, "toPort", string.Empty),
                    Position = ReadEnum(option, "position", SwitchPosition.Straight)
                });
            }

            foreach (var property in element.Element("Properties")?.Elements("Property") ?? [])
            {
                var key = ReadString(property, "key", string.Empty);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                symbol.Properties[key] = ReadString(property, "value", string.Empty);
            }

            document.Symbols.Add(symbol);
        }

        foreach (var element in root.Element("Connections")?.Elements("Connection") ?? [])
        {
            document.Connections.Add(new DrawnTrackConnection
            {
                FromSymbolId = ReadString(element, "fromSymbolId", string.Empty),
                FromPort = ReadString(element, "fromPort", string.Empty),
                ToSymbolId = ReadString(element, "toSymbolId", string.Empty),
                ToPort = ReadString(element, "toPort", string.Empty),
                Cost = ReadInt(element, "cost", 1),
                IsEnabled = ReadBool(element, "isEnabled", true),
                IsBidirectional = ReadBool(element, "isBidirectional", true)
            });
        }

        return document;
    }

    public void Save(string filePath, TrackPlanDocument document)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var xml = new XDocument(
            new XElement("TrackPlan",
                new XAttribute("name", document.Name),
                new XAttribute("version", document.Version.ToString(CultureInfo.InvariantCulture)),
                new XElement("Symbols",
                    document.Symbols
                        .OrderBy(static symbol => symbol.Kind)
                        .ThenBy(static symbol => symbol.Name)
                        .ThenBy(static symbol => symbol.Id)
                        .Select(symbol =>
                        new XElement("Symbol",
                            new XAttribute("id", symbol.Id),
                            new XAttribute("name", symbol.Name),
                            new XAttribute("kind", symbol.Kind.ToString()),
                            new XAttribute("x", symbol.X.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("y", symbol.Y.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("signalDirection", symbol.SignalDirection.ToString()),
                            new XAttribute("currentSwitchPosition", symbol.CurrentSwitchPosition.ToString()),
                            new XElement("SwitchRouteOptions",
                                symbol.SwitchRouteOptions.Select(option =>
                                    new XElement("Option",
                                        new XAttribute("fromPort", option.FromPort),
                                        new XAttribute("toPort", option.ToPort),
                                        new XAttribute("position", option.Position.ToString())))),
                            new XElement("Properties",
                                symbol.Properties
                                    .OrderBy(static property => property.Key)
                                    .Select(property =>
                                        new XElement("Property",
                                            new XAttribute("key", property.Key),
                                            new XAttribute("value", property.Value))))))),
                new XElement("Connections",
                    document.Connections
                        .OrderBy(static connection => connection.FromSymbolId)
                        .ThenBy(static connection => connection.FromPort)
                        .ThenBy(static connection => connection.ToSymbolId)
                        .ThenBy(static connection => connection.ToPort)
                        .Select(connection =>
                        new XElement("Connection",
                            new XAttribute("fromSymbolId", connection.FromSymbolId),
                            new XAttribute("fromPort", connection.FromPort),
                            new XAttribute("toSymbolId", connection.ToSymbolId),
                            new XAttribute("toPort", connection.ToPort),
                            new XAttribute("cost", connection.Cost.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("isEnabled", connection.IsEnabled.ToString()),
                            new XAttribute("isBidirectional", connection.IsBidirectional.ToString()))))));

        SaveFormatted(xml, filePath);
    }

    private static void SaveFormatted(XDocument xml, string filePath)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = Environment.NewLine,
            NewLineHandling = NewLineHandling.Replace,
            NewLineOnAttributes = false
        };

        using var writer = XmlWriter.Create(filePath, settings);
        xml.Save(writer);
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
