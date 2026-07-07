using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OTD.TrainDriving.RouteControl.Tests;

public static class RailwayLayoutDirectedTable
{
    private static readonly Regex TokenRegex = new(@"\[([^\]]+)\]", RegexOptions.Compiled);

    public static void Run()
    {
        var layoutPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "AppData", "railwaylayout.xml"); 

        if (!File.Exists(layoutPath))
        {
            Console.WriteLine($"Datei nicht gefunden: {layoutPath}");
            return;
        }

        var doc = XDocument.Load(layoutPath);
        var trackelements = doc.Root?
            .Element("trackelements")?
            .Elements("trackelement")
            .ToList() ?? new List<XElement>();

        var order = trackelements
            .Select(x => (string?)x.Attribute("id"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();

        // Alle gerichteten Kanten je Element sammeln (fromTokens -> toTokens)
        var edgesByElement = new Dictionary<string, List<(List<string> from, List<string> to)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in order)
            edgesByElement[id] = new List<(List<string> from, List<string> to)>();

        foreach (var te in trackelements)
        {
            var id = (string?)te.Attribute("id");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            foreach (var (fromRaw, toRaw) in GetEdges(te))
            {
                var from = ExtractValidTokens(fromRaw).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var to = ExtractValidTokens(toRaw).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                // Leere Kanten überspringen
                if (from.Count == 0 && to.Count == 0)
                    continue;

                edgesByElement[id].Add((from, to));
            }
        }

        // Index: nodeToken -> Elemente, die diesen Token auf FROM-Seite haben
        var fromIndex = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in edgesByElement)
        {
            var elementId = kv.Key;
            foreach (var edge in kv.Value)
            {
                foreach (var fromToken in edge.from)
                {
                    if (!fromIndex.TryGetValue(fromToken, out var owners))
                    {
                        owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        fromIndex[fromToken] = owners;
                    }
                    owners.Add(elementId);
                }
            }
        }

        // Gerichtete Nachfolger: A -> B, wenn A.toToken == B.fromToken
        var successors = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in order)
            successors[id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in edgesByElement)
        {
            var sourceElement = kv.Key;
            foreach (var edge in kv.Value)
            {
                foreach (var toToken in edge.to)
                {
                    if (!fromIndex.TryGetValue(toToken, out var targets))
                        continue;

                    foreach (var target in targets)
                    {
                        if (!target.Equals(sourceElement, StringComparison.OrdinalIgnoreCase))
                            successors[sourceElement].Add(target);
                    }
                }
            }
        }

        Console.WriteLine("=== Gerichtete Pfad-Tabelle (Element -> Nachfolger), NC* ignoriert ===");
        foreach (var id in order)
        {
            var next = successors[id]
                .OrderBy(x => order.IndexOf(x))
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Console.WriteLine($"{id,-8} -> {(next.Count == 0 ? "(keine)" : string.Join(", ", next))}");
        }

        Console.WriteLine();
        Console.WriteLine("=== Hinweis: Elemente ohne Nachfolger ===");
        var deadEnds = order.Where(id => successors[id].Count == 0).ToList();
        if (deadEnds.Count == 0)
            Console.WriteLine("(keine)");
        else
            deadEnds.ForEach(id => Console.WriteLine($"- {id}"));
    }

    private static IEnumerable<(string? from, string? to)> GetEdges(XElement trackelement)
    {
        var type = ((string?)trackelement.Attribute("type"))?.Trim();

        if (string.Equals(type, "simpletrack", StringComparison.OrdinalIgnoreCase))
        {
            yield return ((string?)trackelement.Attribute("from"), (string?)trackelement.Attribute("to"));
            yield break;
        }

        foreach (var path in trackelement.Elements("path"))
            yield return ((string?)path.Attribute("from"), (string?)path.Attribute("to"));
    }

    private static IEnumerable<string> ExtractValidTokens(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            yield break;

        foreach (Match m in TokenRegex.Matches(endpoint))
        {
            var token = m.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(token))
                continue;

            if (token.StartsWith("NC", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return token;
        }
    }
}