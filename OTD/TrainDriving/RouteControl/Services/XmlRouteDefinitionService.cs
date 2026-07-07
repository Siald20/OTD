// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using OTD.Common;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Exceptions;

namespace OTD.TrainDriving.RouteControl.Services;

public sealed class XmlRouteDefinitionService : IRouteDefinitionService
{
    private const string ConfigFileName = "topology.xml";

    private readonly object _sync = new();
    private readonly string _configPath;
    private readonly IRailwayLayoutService _trackLayoutService;
    private Dictionary<(string A, string B), RouteSegment> _segments = new();

    public event Action? DefinitionsChanged;

    public XmlRouteDefinitionService(string? configFilePath = null)
        : this(new XmlRailwayLayoutService(topologyFilePath: configFilePath ?? GetDefaultConfigFilePath()), configFilePath)
    {
    }

    public XmlRouteDefinitionService(IRailwayLayoutService? trackLayoutService, string? configFilePath = null)
    {
        _trackLayoutService = trackLayoutService ?? new XmlRailwayLayoutService(
            topologyFilePath: configFilePath ?? GetDefaultConfigFilePath());
        _configPath = string.IsNullOrWhiteSpace(configFilePath)
            ? GetDefaultConfigFilePath()
            : Path.GetFullPath(configFilePath);

        _trackLayoutService.DefinitionsChanged += OnRailwayLayoutDefinitionsChanged;
        Reload();
    }

    public bool TryGetSegment(string fromWaypointId, string toWaypointId, out RouteSegment segment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromWaypointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toWaypointId);

        var key = CreateUndirectedKey(fromWaypointId, toWaypointId);
        lock (_sync)
        {
            return _segments.TryGetValue(key, out segment!);
        }
    }

    public IReadOnlyCollection<RouteSegment> GetAllSegments()
    {
        lock (_sync)
        {
            return _segments.Values.ToList().AsReadOnly();
        }
    }

    public void Reload()
    {
        var loaded = LoadSegmentsFromFile(_trackLayoutService, _configPath);
        lock (_sync)
        {
            _segments = loaded;
        }

        Logging.Info<XmlRouteDefinitionService>($"Route definition config loaded: segments={loaded.Count}, file='{_configPath}'.");
        DefinitionsChanged?.Invoke();
    }

    public static string GetDefaultConfigFilePath()
    {
        var appDataPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "AppData");
        return Path.GetFullPath(Path.Combine(appDataPath, ConfigFileName));
    }

    private void OnRailwayLayoutDefinitionsChanged() => Reload();

    private static Dictionary<(string A, string B), RouteSegment> LoadSegmentsFromFile(
        IRailwayLayoutService trackLayoutService,
        string configPath)
    {
        var generatedLegs = trackLayoutService.GetAllGeneratedLegs();
        var generatedLegsByDirectedKey = generatedLegs.ToDictionary(
            leg => CreateDirectedKey(leg.FromWaypointId, leg.ToWaypointId),
            leg => leg);
        var generatedDirectedKeys = new HashSet<(string From, string To)>(
            generatedLegs.Select(leg => CreateDirectedKey(leg.FromWaypointId, leg.ToWaypointId)));

        var segments = ParseSegmentsFromFile(configPath);
        var result = new Dictionary<(string A, string B), RouteSegment>();

        foreach (var segment in segments)
        {
            var directKey = CreateDirectedKey(segment.FromWaypointId, segment.ToWaypointId);
            var reverseKey = CreateDirectedKey(segment.ToWaypointId, segment.FromWaypointId);
            if (!generatedDirectedKeys.Contains(directKey) && !generatedDirectedKeys.Contains(reverseKey))
            {
                throw new RouteValidationException(
                    $"routesegment '{segment.FromWaypointId}->{segment.ToWaypointId}' is not a neighboring waypoint segment in railwaylayout.xml.");
            }

            var undirectedKey = CreateUndirectedKey(segment.FromWaypointId, segment.ToWaypointId);
            if (result.ContainsKey(undirectedKey))
            {
                throw new RouteValidationException(
                    $"Duplicate routesegment override for pair '{segment.FromWaypointId}<->{segment.ToWaypointId}'.");
            }

            var lengthCm = ResolveSegmentLengthCm(segment.FromWaypointId, segment.ToWaypointId, generatedLegsByDirectedKey);

            result[undirectedKey] = new RouteSegment(
                FromWaypointId: segment.FromWaypointId,
                ToWaypointId: segment.ToWaypointId,
                LengthCm: lengthCm,
                MaxSpeedByClassKmh: segment.Defaults.MaxSpeedByClassKmh);
        }

        return result;
    }

    private static int ResolveSegmentLengthCm(
        string fromWaypointId,
        string toWaypointId,
        IReadOnlyDictionary<(string From, string To), GeneratedTrackLeg> generatedLegsByDirectedKey)
    {
        var directKey = CreateDirectedKey(fromWaypointId, toWaypointId);
        if (generatedLegsByDirectedKey.TryGetValue(directKey, out var directLeg))
            return directLeg.DistanceCm;

        var reverseKey = CreateDirectedKey(toWaypointId, fromWaypointId);
        if (generatedLegsByDirectedKey.TryGetValue(reverseKey, out var reverseLeg))
            return reverseLeg.DistanceCm;

        throw new RouteValidationException(
            $"Unable to derive length for routesegment '{fromWaypointId}<->{toWaypointId}' from railwaylayout topology.");
    }

    private static List<RouteOverrideDefinition> ParseSegmentsFromFile(string configPath)
    {
        var overrides = new List<RouteOverrideDefinition>();
        if (!File.Exists(configPath))
            return overrides;

        XDocument document;
        try
        {
            document = XDocument.Load(configPath);
        }
        catch (Exception ex)
        {
            throw new RouteValidationException($"Failed to load route definition file '{configPath}': {ex.Message}");
        }

        var root = document.Root;
        if (root is null)
            throw new RouteValidationException("topology.xml must contain a root element.");

        var rootName = root.Name.LocalName;
        var isTopologyRoot = string.Equals(rootName, "topology", StringComparison.OrdinalIgnoreCase);
        var isRouteSegmentsRoot = string.Equals(rootName, "routesegments", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(rootName, "routesegemnts", StringComparison.OrdinalIgnoreCase);
        var isLegacyRouteLegsRoot = string.Equals(rootName, "routelegs", StringComparison.OrdinalIgnoreCase);
        if (!isTopologyRoot && !isRouteSegmentsRoot && !isLegacyRouteLegsRoot)
            throw new RouteValidationException("topology.xml root element must be <topology> (legacy: <routesegments> or <routelegs>)." );

        var segmentElementName = isTopologyRoot ? "segment" : (isRouteSegmentsRoot ? "routesegment" : "routeleg");
        foreach (var routeLegElement in root.Elements(segmentElementName))
        {
            if (routeLegElement.Elements("direction").Any())
            {
                throw new RouteValidationException(
                    "Direction-specific routesegments are not supported.");
            }

            var fromWaypointId = OptionalAttribute(routeLegElement, "from")
                                 ?? OptionalAttribute(routeLegElement, "waypoint1")
                                  ?? throw new RouteValidationException("routesegment requires from (legacy: waypoint1).");
            var toWaypointId = OptionalAttribute(routeLegElement, "to")
                               ?? OptionalAttribute(routeLegElement, "waypoint2")
                               ?? throw new RouteValidationException("routesegment requires to (legacy: waypoint2).");
            if (string.Equals(fromWaypointId.Trim(), toWaypointId.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new RouteValidationException($"routesegment '{fromWaypointId}' requires distinct from/to.");

            if (!string.IsNullOrWhiteSpace(OptionalAttribute(routeLegElement, "intermediate")))
            {
                throw new RouteValidationException(
                    $"routesegment '{fromWaypointId}->{toWaypointId}' must not define intermediate='...'. Only neighboring segments are allowed.");
            }

            var parsedDefaults = ParseDefaults(routeLegElement);
            var undirectedKey = CreateUndirectedKey(fromWaypointId, toWaypointId);
            if (overrides.Any(existing => CreateUndirectedKey(existing.FromWaypointId, existing.ToWaypointId) == undirectedKey))
            {
                throw new RouteValidationException(
                    $"Duplicate routesegment override for pair '{fromWaypointId}<->{toWaypointId}'.");
            }

            overrides.Add(new RouteOverrideDefinition(
                FromWaypointId: fromWaypointId.Trim(),
                ToWaypointId: toWaypointId.Trim(),
                Defaults: parsedDefaults));
        }

        return overrides;
    }

    private static RouteOverride ParseDefaults(XElement routeLegElement)
    {
        var defaultsElement = routeLegElement.Element("defaults");
        var speedBySpeedClass = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Tolerant parser: only speedlimits are evaluated, all other elements are ignored.
        var speedLimitsElements = routeLegElement.Elements("speedlimits")
            .Concat(defaultsElement?.Elements("speedlimits") ?? Enumerable.Empty<XElement>());

        foreach (var speedLimitsElement in speedLimitsElements)
        {
            foreach (var speedLimitElement in speedLimitsElement.Elements("speedlimit"))
            {
                var speedClass = OptionalAttribute(speedLimitElement, "speedClass")
                                 ?? OptionalAttribute(speedLimitElement, "category")
                                 ?? OptionalAttribute(speedLimitElement, "catetory")
                                 ?? "default";
                var speedKmh = ParseIntAttribute(speedLimitElement, "speed_kmh")
                               ?? ParseIntAttribute(speedLimitElement, "speed");
                if (speedKmh is null)
                {
                    var speedAsDouble = ParseDoubleAttribute(speedLimitElement, "speed_kmh")
                                       ?? ParseDoubleAttribute(speedLimitElement, "speed")
                                       ?? throw new RouteValidationException($"speedlimit for speedClass '{speedClass}' requires speed_kmh.");
                    speedKmh = (int)Math.Round(speedAsDouble, MidpointRounding.AwayFromZero);
                }
                if (speedKmh <= 0)
                    throw new RouteValidationException($"speedlimit for speedClass '{speedClass}' must be > 0.");

                speedBySpeedClass[speedClass.Trim()] = speedKmh.Value;
            }
        }

        return new RouteOverride(speedBySpeedClass.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
    }


    private static (string From, string To) CreateDirectedKey(string fromWaypointId, string toWaypointId)
    {
        return (NormalizeKey(fromWaypointId), NormalizeKey(toWaypointId));
    }

    private static (string A, string B) CreateUndirectedKey(string waypointA, string waypointB)
    {
        var a = NormalizeKey(waypointA);
        var b = NormalizeKey(waypointB);
        return string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
    }

    private static string NormalizeKey(string value) => value.Trim().ToUpperInvariant();

    private static string? OptionalAttribute(XElement? element, string name)
    {
        var attribute = element?.Attribute(name);
        return attribute is null ? null : attribute.Value.Trim();
    }

    private static int? ParseIntAttribute(XElement? element, string name)
    {
        var raw = OptionalAttribute(element, name);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        throw new RouteValidationException($"Invalid integer value '{raw}' for attribute '{name}'.");
    }

    private static double? ParseDoubleAttribute(XElement? element, string name)
    {
        var raw = OptionalAttribute(element, name);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        throw new RouteValidationException($"Invalid numeric value '{raw}' for attribute '{name}'.");
    }

    private sealed record RouteOverride(
        IReadOnlyDictionary<string, int> MaxSpeedByClassKmh);

    private sealed record RouteOverrideDefinition(
        string FromWaypointId,
        string ToWaypointId,
        RouteOverride Defaults);
}


