using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OTD.TrainDriving.RouteControl.Tests;

public static class RailwayLayoutNeighborTable
{
    // Extracts bracketed tokens from strings like "[W1:2][G3-1:1]"
    private static readonly Regex TokenRegex = new(@"\[([^\]]+)\]", RegexOptions.Compiled);

    public static void Run()
    {
        // layout path
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

        // Keep declaration order for "oben nach unten" output
        var elementOrder = trackelements
            .Select(te => (string?)te.Attribute("id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToList();

        // token -> set of owning elements that mention this token
        var tokenOwners = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        // element -> all tokens mentioned in its own edge definitions
        var elementTokens = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var te in trackelements)
        {
            var elementId = (string?)te.Attribute("id");
            if (string.IsNullOrWhiteSpace(elementId))
                continue;

            var edges = GetEdges(te); // handles simpletrack(from/to) + turnout(path from/to)
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (from, to) in edges)
            {
                foreach (var token in ExtractValidTokens(from).Concat(ExtractValidTokens(to)))
                {
                    tokens.Add(token);

                    if (!tokenOwners.TryGetValue(token, out var owners))
                    {
                        owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        tokenOwners[token] = owners;
                    }
                    owners.Add(elementId);
                }
            }

            elementTokens[elementId] = tokens;
        }

        // Build neighbor table: element -> other elements sharing at least one non-NC token
        var neighbors = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in elementOrder)
            neighbors[id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in elementOrder)
        {
            foreach (var token in elementTokens.GetValueOrDefault(id, new HashSet<string>()))
            {
                foreach (var other in tokenOwners.GetValueOrDefault(token, new HashSet<string>()))
                {
                    if (!other.Equals(id, StringComparison.OrdinalIgnoreCase))
                        neighbors[id].Add(other);
                }
            }
        }

        // Print table in declaration order
        Console.WriteLine("=== Element -> erreichbare Nachfolger (NC* ignoriert) ===");
        foreach (var id in elementOrder)
        {
            var list = neighbors[id]
                .OrderBy(x => elementOrder.IndexOf(x))
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Console.WriteLine($"{id,-8} -> {(list.Count == 0 ? "(keine)" : string.Join(", ", list))}");
        }

        // Optional consistency hints
        var isolated = elementOrder.Where(id => neighbors[id].Count == 0).ToList();
        if (isolated.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("WARNUNG: Isolierte Elemente gefunden:");
            foreach (var id in isolated)
                Console.WriteLine($" - {id}");
        }
    }

    private static IEnumerable<(string? from, string? to)> GetEdges(XElement trackelement)
    {
        var type = ((string?)trackelement.Attribute("type"))?.Trim();

        // simpletrack: edge on trackelement itself
        if (string.Equals(type, "simpletrack", StringComparison.OrdinalIgnoreCase))
        {
            yield return ((string?)trackelement.Attribute("from"), (string?)trackelement.Attribute("to"));
            yield break;
        }

        // turnout variants: edges on nested path elements
        foreach (var path in trackelement.Elements("path"))
        {
            yield return ((string?)path.Attribute("from"), (string?)path.Attribute("to"));
        }
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

            // Ignore placeholders like NC1, NC2, ...
            if (token.StartsWith("NC", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return token;
        }
    }
}