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

public sealed class XmlRailwayLayoutService : IRailwayLayoutService
{
    private const string ConfigFileName = "railwaylayout.xml";
    private const string TopologyConfigFileName = "topology.xml";

    private readonly object _sync = new();
    private readonly string _configPath;
    private readonly string? _topologyPath;
    private Dictionary<(string From, string To), GeneratedTrackLeg> _generatedLegs = new();
    private Dictionary<string, TrackFeedbackSection> _sections = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, TrackFeedbackPoint> _points = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, TrackWaypoint> _waypoints = new(StringComparer.OrdinalIgnoreCase);

    public event Action? DefinitionsChanged;

    public XmlRailwayLayoutService(string? configFilePath = null, string? topologyFilePath = null)
    {
        _configPath = string.IsNullOrWhiteSpace(configFilePath)
            ? GetDefaultConfigFilePath()
            : Path.GetFullPath(configFilePath);
        _topologyPath = string.IsNullOrWhiteSpace(topologyFilePath)
            ? GetDefaultTopologyFilePath()
            : Path.GetFullPath(topologyFilePath);

        Reload();
    }

    public bool TryGetGeneratedLeg(string fromWaypointId, string toWaypointId, out GeneratedTrackLeg leg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromWaypointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toWaypointId);

        var key = (NormalizeKey(fromWaypointId), NormalizeKey(toWaypointId));
        lock (_sync)
        {
            return _generatedLegs.TryGetValue(key, out leg!);
        }
    }

    public bool TryGetSection(string sectionId, out TrackFeedbackSection section)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);

        lock (_sync)
        {
            return _sections.TryGetValue(sectionId.Trim(), out section!);
        }
    }

    public bool TryGetPoint(string pointId, out TrackFeedbackPoint point)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pointId);

        lock (_sync)
        {
            return _points.TryGetValue(pointId.Trim(), out point!);
        }
    }

    public bool TryGetWaypoint(string waypointId, out TrackWaypoint waypoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(waypointId);

        lock (_sync)
        {
            return _waypoints.TryGetValue(waypointId.Trim(), out waypoint!);
        }
    }

    public IReadOnlyCollection<GeneratedTrackLeg> GetAllGeneratedLegs()
    {
        lock (_sync)
        {
            return _generatedLegs.Values.ToList().AsReadOnly();
        }
    }

    public IReadOnlyCollection<TrackFeedbackSection> GetAllSections()
    {
        lock (_sync)
        {
            return _sections.Values.ToList().AsReadOnly();
        }
    }

    public IReadOnlyCollection<TrackFeedbackPoint> GetAllPoints()
    {
        lock (_sync)
        {
            return _points.Values.ToList().AsReadOnly();
        }
    }

    public IReadOnlyCollection<TrackWaypoint> GetAllWaypoints()
    {
        lock (_sync)
        {
            return _waypoints.Values.ToList().AsReadOnly();
        }
    }

    public void Reload()
    {
        var snapshot = LoadFromFile(_configPath, _topologyPath);
        lock (_sync)
        {
            _generatedLegs = snapshot.GeneratedLegs;
            _sections = snapshot.Sections;
            _points = snapshot.Points;
            _waypoints = snapshot.Waypoints;
        }

        Logging.Info<XmlRailwayLayoutService>(
            $"Railway layout config loaded: waypoints={snapshot.Waypoints.Count}, sections={snapshot.Sections.Count}, points={snapshot.Points.Count}, legs={snapshot.GeneratedLegs.Count}, file='{_configPath}'.");
        DefinitionsChanged?.Invoke();
    }

    public static string GetDefaultConfigFilePath()
    {
        var appDataPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "AppData");
        return Path.GetFullPath(Path.Combine(appDataPath, ConfigFileName));
    }

    public static string GetDefaultTopologyFilePath()
    {
        var appDataPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "AppData");
        return Path.GetFullPath(Path.Combine(appDataPath, TopologyConfigFileName));
    }

    private static LayoutSnapshot LoadFromFile(string configPath, string? topologyPath)
    {
        if (!File.Exists(configPath))
            throw new RouteValidationException($"Railway layout file not found: {configPath}");

        XDocument document;
        try
        {
            document = XDocument.Load(configPath);
        }
        catch (Exception ex)
        {
            throw new RouteValidationException($"Failed to load railway layout file '{configPath}': {ex.Message}");
        }

        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "railwaylayout", StringComparison.OrdinalIgnoreCase))
            throw new RouteValidationException("railwaylayout.xml root element must be <railwaylayout>.");

        var edges = ParseEdges(root);
        var usedDetectorIds = new HashSet<int>();
        var sections = ParseSections(root, edges, usedDetectorIds);
        var points = ParsePoints(root, edges, usedDetectorIds);
        var waypointsSource = ResolveWaypointsElement(root, configPath, topologyPath);
        var waypoints = ParseWaypoints(waypointsSource, edges);
        var generatedLegs = GenerateLegs(edges, sections, points, waypoints);

        return new LayoutSnapshot(generatedLegs, sections, points, waypoints);
    }

    /// <summary>
    /// Resolves the XElement containing &lt;waypoints&gt;.
    /// Checks railwaylayout.xml first; if absent, falls back to topology.xml.
    /// </summary>
    private static XElement ResolveWaypointsElement(XElement layoutRoot, string layoutPath, string? topologyPath)
    {
        var inLayout = layoutRoot.Element("waypoints");
        if (inLayout is not null)
            return layoutRoot;

        // Waypoints are defined in topology.xml (preferred new approach)
        if (!string.IsNullOrWhiteSpace(topologyPath) && File.Exists(topologyPath))
        {
            XDocument topologyDoc;
            try
            {
                topologyDoc = XDocument.Load(topologyPath);
            }
            catch (Exception ex)
            {
                throw new RouteValidationException(
                    $"Failed to load topology file '{topologyPath}' for waypoints: {ex.Message}");
            }

            var topologyRoot = topologyDoc.Root;
            if (topologyRoot is not null && topologyRoot.Element("waypoints") is not null)
                return topologyRoot;
        }

        throw new RouteValidationException(
            $"No <waypoints> section found in '{layoutPath}' or in topology file '{topologyPath ?? "(none)"}'. " +
            "Define waypoints either in railwaylayout.xml or in topology.xml.");
    }

    private static Dictionary<string, TrackEdge> ParseEdges(XElement root)    {
        var edges = new Dictionary<string, TrackEdge>(StringComparer.OrdinalIgnoreCase);

        var trackElementsElement = root.Element("trackelements");
        if (trackElementsElement is not null)
        {
            foreach (var element in trackElementsElement.Elements())
            {
                switch (element.Name.LocalName.ToLowerInvariant())
                {
                    case "trackelement":
                    {
                        var elementId = RequireAttribute(element, "id", "trackelement");
                        var type = (OptionalAttribute(element, "type") ?? "simpletrack").Trim().ToLowerInvariant();
                        var pathElements = element.Elements("path").ToList();

                        switch (type)
                        {
                            case "simpletrack":
                            case "track":
                            case "segment":
                                if (pathElements.Count > 0)
                                {
                                    throw new RouteValidationException(
                                        $"trackelement '{elementId}' of type '{type}' must not contain <path>. Use type='turnout' or type='crossing' for multi-path elements.");
                                }

                                var edge = ParseEdge(element, elementKind: "trackelement", parentElementId: elementId);
                                if (!edges.TryAdd(edge.Id, edge))
                                    throw new RouteValidationException($"Duplicate track edge id '{edge.Id}'.");
                                break;

                            case "turnout":
                            case "switch":
                                if (OptionalAttribute(element, "from") is not null ||
                                    OptionalAttribute(element, "to") is not null ||
                                    OptionalAttribute(element, "length_cm") is not null)
                                {
                                    throw new RouteValidationException(
                                        $"trackelement '{elementId}' of type '{type}' must not define from/to/length_cm directly. Define traversable alternatives via <path> nodes.");
                                }

                                if (pathElements.Count == 0)
                                    throw new RouteValidationException($"trackelement '{elementId}' of type '{type}' requires at least one <path>.");

                                foreach (var pathElement in pathElements)
                                {
                                    var pathEdge = ParseEdge(pathElement, elementKind: "turnout_path", parentElementId: elementId);
                                    if (!edges.TryAdd(pathEdge.Id, pathEdge))
                                        throw new RouteValidationException($"Duplicate track edge id '{pathEdge.Id}'.");
                                }

                                break;

                            case "crossing":
                                if (OptionalAttribute(element, "from") is not null ||
                                    OptionalAttribute(element, "to") is not null ||
                                    OptionalAttribute(element, "length_cm") is not null)
                                {
                                    throw new RouteValidationException(
                                        $"trackelement '{elementId}' of type 'crossing' must not define from/to/length_cm directly. Define traversable alternatives via <path> nodes.");
                                }

                                if (pathElements.Count == 0)
                                    throw new RouteValidationException($"trackelement '{elementId}' of type 'crossing' requires at least one <path>.");

                                foreach (var pathElement in pathElements)
                                {
                                    var pathEdge = ParseEdge(pathElement, elementKind: "crossing_path", parentElementId: elementId);
                                    if (!edges.TryAdd(pathEdge.Id, pathEdge))
                                        throw new RouteValidationException($"Duplicate track edge id '{pathEdge.Id}'.");
                                }

                                break;

                            default:
                                throw new RouteValidationException(
                                    $"trackelement '{elementId}' has unsupported type '{type}'. Valid types: simpletrack, turnout, crossing.");
                        }

                        break;
                    }

                    case "switch":
                    {
                        var switchId = RequireAttribute(element, "id", "switch");
                        if (OptionalAttribute(element, "from") is not null ||
                            OptionalAttribute(element, "to") is not null ||
                            OptionalAttribute(element, "length_cm") is not null)
                        {
                            throw new RouteValidationException(
                                $"switch '{switchId}' must not define from/to/length_cm directly. Define traversable alternatives via <path> nodes.");
                        }

                        var paths = element.Elements("path").ToList();
                        if (paths.Count == 0)
                            throw new RouteValidationException($"switch '{switchId}' requires at least one <path>.");

                        foreach (var pathElement in paths)
                        {
                            var edge = ParseEdge(pathElement, elementKind: "switch_path", parentElementId: switchId);
                            if (!edges.TryAdd(edge.Id, edge))
                                throw new RouteValidationException($"Duplicate track edge id '{edge.Id}'.");
                        }
                        break;
                    }

                    case "crossing":
                    {
                        var crossingId = RequireAttribute(element, "id", "crossing");
                        var paths = element.Elements("path").ToList();
                        if (paths.Count == 0)
                            throw new RouteValidationException($"crossing '{crossingId}' requires at least one <path>.");

                        foreach (var pathElement in paths)
                        {
                            var edge = ParseEdge(pathElement, elementKind: "crossing_path", parentElementId: crossingId);
                            if (!edges.TryAdd(edge.Id, edge))
                                throw new RouteValidationException($"Duplicate track edge id '{edge.Id}'.");
                        }
                        break;
                    }

                    default:
                        throw new RouteValidationException($"Unsupported trackelement '{element.Name.LocalName}'. Valid children of <trackelements>: trackelement, switch, crossing.");
                }
            }
        }
        else
        {
            // Legacy schema fallback: <segments>/<switches>/<crossings> on root.
            var edgeContainers = root.Elements().Where(e =>
                string.Equals(e.Name.LocalName, "segments", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Name.LocalName, "switches", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Name.LocalName, "crossings", StringComparison.OrdinalIgnoreCase));

            foreach (var container in edgeContainers)
            {
                switch (container.Name.LocalName.ToLowerInvariant())
                {
                    case "segments":
                        foreach (var segmentElement in container.Elements("segment"))
                        {
                            var edge = ParseEdge(segmentElement, elementKind: "segment", parentElementId: RequireAttribute(segmentElement, "id", "segment"));
                            if (!edges.TryAdd(edge.Id, edge))
                                throw new RouteValidationException($"Duplicate track edge id '{edge.Id}'.");
                        }
                        break;

                    case "switches":
                        foreach (var switchElement in container.Elements("switch"))
                        {
                            var switchId = RequireAttribute(switchElement, "id", "switch");
                            foreach (var pathElement in switchElement.Elements("path"))
                            {
                                var edge = ParseEdge(pathElement, elementKind: "switch_path", parentElementId: switchId);
                                if (!edges.TryAdd(edge.Id, edge))
                                    throw new RouteValidationException($"Duplicate track edge id '{edge.Id}'.");
                            }
                        }
                        break;

                    case "crossings":
                        foreach (var crossingElement in container.Elements("crossing"))
                        {
                            var crossingId = RequireAttribute(crossingElement, "id", "crossing");
                            foreach (var pathElement in crossingElement.Elements("path"))
                            {
                                var edge = ParseEdge(pathElement, elementKind: "crossing_path", parentElementId: crossingId);
                                if (!edges.TryAdd(edge.Id, edge))
                                    throw new RouteValidationException($"Duplicate track edge id '{edge.Id}'.");
                            }
                        }
                        break;
                }
            }
        }

        if (edges.Count == 0)
            throw new RouteValidationException("railwaylayout.xml must define at least one traversable edge via <trackelements>/<trackelement> (or legacy <segments>, <switches>, <crossings>)." );

        return edges;
    }

    private static Dictionary<string, TrackFeedbackSection> ParseSections(
        XElement root,
        IReadOnlyDictionary<string, TrackEdge> edges,
        ISet<int> usedDetectorIds)
    {
        var sections = new Dictionary<string, TrackFeedbackSection>(StringComparer.OrdinalIgnoreCase);
        var sensorsElement = root.Element("sensors");
        if (sensorsElement is null)
            return sections;

        foreach (var sectionElement in sensorsElement.Elements("section"))
        {
            var id = RequireAttribute(sectionElement, "id", "sensor section");
            var detectorId = ParseIntAttribute(sectionElement, "detectorId")
                ?? ParseIntAttribute(sectionElement, "detector_id")
                ?? throw new RouteValidationException($"sensor section '{id}' requires detectorId.");
            if (!usedDetectorIds.Add(detectorId))
                throw new RouteValidationException($"Duplicate detectorId '{detectorId}' in railwaylayout sensors.");

            var hostTrackId = ResolveHostTrackId(sectionElement, $"sensor section '{id}'");
            ResolveSectionHostEdgeIds(edges, hostTrackId, $"sensor section '{id}'");

            var description = OptionalAttribute(sectionElement, "description");
            var section = new TrackFeedbackSection(id, detectorId, hostTrackId, SensorType.OccupancyDetection, description);
            if (!sections.TryAdd(section.Id, section))
                throw new RouteValidationException($"Duplicate sensor section id '{section.Id}'.");
        }

        return sections;
    }

    private static Dictionary<string, TrackFeedbackPoint> ParsePoints(
        XElement root,
        IReadOnlyDictionary<string, TrackEdge> edges,
        ISet<int> usedDetectorIds)
    {
        var points = new Dictionary<string, TrackFeedbackPoint>(StringComparer.OrdinalIgnoreCase);
        var sensorsElement = root.Element("sensors");
        if (sensorsElement is null)
            return points;

        foreach (var pointElement in sensorsElement.Elements("point"))
        {
            var id = RequireAttribute(pointElement, "id", "sensor point");
            var detectorId = ParseIntAttribute(pointElement, "detectorId")
                ?? ParseIntAttribute(pointElement, "detector_id")
                ?? throw new RouteValidationException($"sensor point '{id}' requires detectorId.");
            if (!usedDetectorIds.Add(detectorId))
                throw new RouteValidationException($"Duplicate detectorId '{detectorId}' in railwaylayout sensors.");

            var hostTrackId = ResolveHostTrackId(pointElement, $"sensor point '{id}'");
            var offsetCm = ParseIntAttribute(pointElement, "offset_cm")
                ?? ParseIntAttribute(pointElement, "offset")
                ?? throw new RouteValidationException($"sensor point '{id}' requires offset_cm.");
            var hostEdgeIds = ResolveHostEdgeIdsForOffset(edges, hostTrackId, offsetCm, $"sensor point '{id}'");

            foreach (var hostEdgeId in hostEdgeIds)
            {
                var edge = GetRequiredEdge(edges, hostEdgeId, $"sensor point '{id}'");
                if (offsetCm < 0 || offsetCm > edge.LengthCm)
                {
                    throw new RouteValidationException(
                        $"sensor point '{id}' must be within [0, {edge.LengthCm}] cm of host '{hostTrackId}' (resolved edge '{edge.Id}').");
                }
            }

            var type = SensorType.TrackContact;
            var description = OptionalAttribute(pointElement, "description");
            var point = new TrackFeedbackPoint(id, detectorId, hostTrackId, offsetCm, type, description);
            if (!points.TryAdd(point.Id, point))
                throw new RouteValidationException($"Duplicate sensor point id '{point.Id}'.");
        }

        return points;
    }

    private static Dictionary<string, TrackWaypoint> ParseWaypoints(
        XElement waypointsSource,
        IReadOnlyDictionary<string, TrackEdge> edges)
    {
        var waypoints = new Dictionary<string, TrackWaypoint>(StringComparer.OrdinalIgnoreCase);
        var usedNodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedEdgeOffsets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var waypointsElement = waypointsSource.Element("waypoints");
        if (waypointsElement is null)
            throw new RouteValidationException("No <waypoints> section found in the resolved waypoint source.");

        foreach (var waypointElement in waypointsElement.Elements("waypoint"))
        {
            var id = RequireAttribute(waypointElement, "id", "waypoint");
            var description = OptionalAttribute(waypointElement, "description");
            var nodeId = OptionalAttribute(waypointElement, "node");
            var hostTrackId = OptionalAttribute(waypointElement, "host")
                              ?? OptionalAttribute(waypointElement, "track")
                              ?? OptionalAttribute(waypointElement, "segment")
                              ?? OptionalAttribute(waypointElement, "segment_id")
                              ?? OptionalAttribute(waypointElement, "path")
                              ?? OptionalAttribute(waypointElement, "path_id");
            var offsetCm = ParseIntAttribute(waypointElement, "offset_cm")
                           ?? ParseIntAttribute(waypointElement, "offset");

            if (!string.IsNullOrWhiteSpace(nodeId) && !string.IsNullOrWhiteSpace(hostTrackId))
            {
                throw new RouteValidationException(
                    $"waypoint '{id}' must use either node='...' or host='...'+offset_cm, not both.");
            }

            TrackWaypoint waypoint;
            if (!string.IsNullOrWhiteSpace(nodeId))
            {
                if (!NodeExists(edges, nodeId.Trim()))
                    throw new RouteValidationException($"waypoint '{id}' references unknown node '{nodeId}'.");
                if (!usedNodeIds.Add(nodeId.Trim()))
                    throw new RouteValidationException($"Only one waypoint per node is supported. Node '{nodeId}' is already used.");
                waypoint = new TrackWaypoint(id, NodeId: nodeId.Trim(), Description: description);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(hostTrackId) || offsetCm is null)
                {
                    throw new RouteValidationException(
                        $"waypoint '{id}' requires either node='...' or host='...'+offset_cm.");
                }

                var hostEdgeIds = ResolveHostEdgeIdsForOffset(edges, hostTrackId, offsetCm.Value, $"waypoint '{id}'");
                if (hostEdgeIds.Count > 1)
                {
                    if (offsetCm != 0)
                    {
                        throw new RouteValidationException(
                            $"waypoint '{id}' uses host '{hostTrackId}' with multiple paths and offset_cm={offsetCm.Value}. Use a specific path id for non-zero offsets.");
                    }

                    var sharedStartNodes = hostEdgeIds
                        .Select(edgeId => GetRequiredEdge(edges, edgeId, $"waypoint '{id}'").FromNodeId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (sharedStartNodes.Count != 1)
                    {
                        throw new RouteValidationException(
                            $"waypoint '{id}' uses host '{hostTrackId}' with multiple paths but no unique start node. Use a specific path id.");
                    }

                    var sharedNodeId = sharedStartNodes[0];
                    if (!usedNodeIds.Add(sharedNodeId))
                        throw new RouteValidationException($"Only one waypoint per node is supported. Node '{sharedNodeId}' is already used.");
                    waypoint = new TrackWaypoint(id, NodeId: sharedNodeId, Description: description);

                    if (!waypoints.TryAdd(waypoint.Id, waypoint))
                        throw new RouteValidationException($"Duplicate waypoint id '{waypoint.Id}'.");
                    continue;
                }

                var edge = GetRequiredEdge(edges, hostEdgeIds[0], $"waypoint '{id}'");
                if (offsetCm < 0 || offsetCm > edge.LengthCm)
                {
                    throw new RouteValidationException(
                        $"waypoint '{id}' must be within [0, {edge.LengthCm}] cm of host '{edge.Id}'.");
                }

                if (offsetCm == 0)
                {
                    if (!usedNodeIds.Add(edge.FromNodeId))
                        throw new RouteValidationException($"Only one waypoint per node is supported. Node '{edge.FromNodeId}' is already used.");
                    waypoint = new TrackWaypoint(id, NodeId: edge.FromNodeId, Description: description);
                }
                else if (offsetCm == edge.LengthCm)
                {
                    if (!usedNodeIds.Add(edge.ToNodeId))
                        throw new RouteValidationException($"Only one waypoint per node is supported. Node '{edge.ToNodeId}' is already used.");
                    waypoint = new TrackWaypoint(id, NodeId: edge.ToNodeId, Description: description);
                }
                else
                {
                    var edgeOffsetKey = $"{edge.Id}:{offsetCm.Value}";
                    if (!usedEdgeOffsets.Add(edgeOffsetKey))
                        throw new RouteValidationException(
                            $"Only one waypoint per track position is supported. Host '{edge.Id}' already has a waypoint at {offsetCm.Value} cm.");
                    waypoint = new TrackWaypoint(id, HostTrackId: edge.Id, OffsetCm: offsetCm.Value, Description: description);
                }
            }

            if (!waypoints.TryAdd(waypoint.Id, waypoint))
                throw new RouteValidationException($"Duplicate waypoint id '{waypoint.Id}'.");
        }

        if (waypoints.Count == 0)
            throw new RouteValidationException("The <waypoints> section must define at least one waypoint.");

        return waypoints;
    }

    private static Dictionary<(string From, string To), GeneratedTrackLeg> GenerateLegs(
        IReadOnlyDictionary<string, TrackEdge> edges,
        IReadOnlyDictionary<string, TrackFeedbackSection> sections,
        IReadOnlyDictionary<string, TrackFeedbackPoint> points,
        IReadOnlyDictionary<string, TrackWaypoint> waypoints)
    {
        var edgesByNode = BuildEdgesByNode(edges);
        var sectionsByEdge = sections.Values
            .SelectMany(section => ResolveSectionHostEdgeIds(edges, section.HostTrackId, $"sensor section '{section.Id}'")
                .Select(edgeId => (EdgeId: edgeId, Section: section)))
            .GroupBy(item => item.EdgeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Section).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var pointsByEdge = points.Values
            .SelectMany(point => ResolveHostEdgeIdsForOffset(edges, point.HostTrackId, point.OffsetCm, $"sensor point '{point.Id}'")
                .Select(edgeId => (EdgeId: edgeId, Point: point)))
            .GroupBy(item => item.EdgeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Point).OrderBy(point => point.OffsetCm).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var edgeWaypoints = waypoints.Values
            .Where(waypoint => waypoint.HostTrackId is not null && waypoint.OffsetCm is not null)
            .GroupBy(waypoint => waypoint.HostTrackId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(waypoint => waypoint.OffsetCm!.Value).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var nodeWaypoints = waypoints.Values
            .Where(waypoint => waypoint.NodeId is not null)
            .ToDictionary(waypoint => waypoint.NodeId!, waypoint => waypoint, StringComparer.OrdinalIgnoreCase);

        var generatedLegs = new Dictionary<(string From, string To), GeneratedTrackLeg>();

        foreach (var waypoint in waypoints.Values.OrderBy(waypoint => waypoint.Id, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var branch in CreateInitialBranches(waypoint, edges, edgesByNode))
            {
                var results = new List<GeneratedTrackLeg>();
                ExploreBranch(
                    startWaypoint: waypoint,
                    edge: branch.Edge,
                    moveForward: branch.MoveForward,
                    startOffsetCm: branch.StartOffsetCm,
                    includeCurrentPositionSensors: branch.IncludeCurrentPositionSensors,
                    enteredFromNodeBoundary: branch.EnteredFromNodeBoundary,
                    distanceFromStartCm: 0,
                    currentMarkers: [],
                    visitedStates: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    edgesByNode: edgesByNode,
                    edgeWaypoints: edgeWaypoints,
                    nodeWaypoints: nodeWaypoints,
                    sectionsByEdge: sectionsByEdge,
                    pointsByEdge: pointsByEdge,
                    results: results);

                foreach (var leg in results)
                {
                    var key = (NormalizeKey(leg.FromWaypointId), NormalizeKey(leg.ToWaypointId));
                    if (!generatedLegs.TryAdd(key, leg))
                    {
                        var current = generatedLegs[key];
                        if (IsPreferredGeneratedLeg(leg, current))
                            generatedLegs[key] = leg;
                    }
                }
            }
        }

        return generatedLegs;
    }

    private static Dictionary<string, List<TrackEdge>> BuildEdgesByNode(IReadOnlyDictionary<string, TrackEdge> edges)
    {
        var result = new Dictionary<string, List<TrackEdge>>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges.Values)
        {
            if (!result.TryGetValue(edge.FromNodeId, out var fromList))
            {
                fromList = new List<TrackEdge>();
                result[edge.FromNodeId] = fromList;
            }
            fromList.Add(edge);

            if (!result.TryGetValue(edge.ToNodeId, out var toList))
            {
                toList = new List<TrackEdge>();
                result[edge.ToNodeId] = toList;
            }
            toList.Add(edge);
        }

        return result;
    }

    private static bool IsPreferredGeneratedLeg(GeneratedTrackLeg candidate, GeneratedTrackLeg current)
    {
        if (candidate.DistanceCm != current.DistanceCm)
            return candidate.DistanceCm < current.DistanceCm;

        var candidateMarkerCount = candidate.SensorMarkers?.Count ?? 0;
        var currentMarkerCount = current.SensorMarkers?.Count ?? 0;
        if (candidateMarkerCount != currentMarkerCount)
            return candidateMarkerCount < currentMarkerCount;

        return string.Compare(candidate.FromWaypointId, current.FromWaypointId, StringComparison.OrdinalIgnoreCase) < 0
               || (string.Equals(candidate.FromWaypointId, current.FromWaypointId, StringComparison.OrdinalIgnoreCase)
                   && string.Compare(candidate.ToWaypointId, current.ToWaypointId, StringComparison.OrdinalIgnoreCase) < 0);
    }

    private static IReadOnlyList<InitialBranch> CreateInitialBranches(
        TrackWaypoint waypoint,
        IReadOnlyDictionary<string, TrackEdge> edges,
        IReadOnlyDictionary<string, List<TrackEdge>> edgesByNode)
    {
        var result = new List<InitialBranch>();
        if (!string.IsNullOrWhiteSpace(waypoint.NodeId))
        {
            if (!edgesByNode.TryGetValue(waypoint.NodeId!, out var incidentEdges))
                return result;

            var outgoingFromNode = incidentEdges
                .Where(edge => string.Equals(edge.FromNodeId, waypoint.NodeId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var incomingToNode = incidentEdges
                .Where(edge => string.Equals(edge.ToNodeId, waypoint.NodeId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // For turnout-root style nodes (one incoming trunk, multiple outgoing branches),
            // seed exploration on outgoing branches only to avoid immediate cycle backtracking.
            var initialEdges = outgoingFromNode.Count >= 2 && incomingToNode.Count >= 1
                ? outgoingFromNode
                : incidentEdges;

            foreach (var edge in initialEdges)
            {
                var moveForward = string.Equals(edge.FromNodeId, waypoint.NodeId, StringComparison.OrdinalIgnoreCase);
                result.Add(new InitialBranch(edge, moveForward, moveForward ? 0 : edge.LengthCm, true, true));
            }

            return result;
        }

        var hostEdge = GetRequiredEdge(edges, waypoint.HostTrackId!, $"waypoint '{waypoint.Id}'");
        var offset = waypoint.OffsetCm!.Value;
        result.Add(new InitialBranch(hostEdge, MoveForward: true, StartOffsetCm: offset, IncludeCurrentPositionSensors: false, EnteredFromNodeBoundary: false));
        result.Add(new InitialBranch(hostEdge, MoveForward: false, StartOffsetCm: offset, IncludeCurrentPositionSensors: false, EnteredFromNodeBoundary: false));
        return result;
    }

    private static void ExploreBranch(
        TrackWaypoint startWaypoint,
        TrackEdge edge,
        bool moveForward,
        int startOffsetCm,
        bool includeCurrentPositionSensors,
        bool enteredFromNodeBoundary,
        int distanceFromStartCm,
        List<SensorMarker> currentMarkers,
        HashSet<string> visitedStates,
        IReadOnlyDictionary<string, List<TrackEdge>> edgesByNode,
        IReadOnlyDictionary<string, List<TrackWaypoint>> edgeWaypoints,
        IReadOnlyDictionary<string, TrackWaypoint> nodeWaypoints,
        IReadOnlyDictionary<string, List<TrackFeedbackSection>> sectionsByEdge,
        IReadOnlyDictionary<string, List<TrackFeedbackPoint>> pointsByEdge,
        List<GeneratedTrackLeg> results)
    {
        var stateKey = $"{edge.Id}:{(moveForward ? "F" : "R")}:{startOffsetCm}";
        if (!visitedStates.Add(stateKey))
        {
            // Prune cyclic branch expansion; other branches may still yield valid legs.
            return;
        }

        var terminalOffsetCm = moveForward ? edge.LengthCm : 0;
        var nextWaypoint = FindNextWaypointOnEdge(startWaypoint.Id, edge.Id, startOffsetCm, moveForward, edgeWaypoints, out var nextWaypointOffsetCm);
        var endOffsetCm = nextWaypointOffsetCm ?? terminalOffsetCm;

        var markers = new List<SensorMarker>(currentMarkers);
        AppendSensorsOnEdge(
            edge,
            moveForward,
            startOffsetCm,
            endOffsetCm,
            distanceFromStartCm,
            includeCurrentPositionSensors,
            enteredFromNodeBoundary,
            sectionsByEdge,
            pointsByEdge,
            markers);

        var travelledDistanceCm = Math.Abs(endOffsetCm - startOffsetCm);
        var totalDistanceCm = distanceFromStartCm + travelledDistanceCm;
        if (nextWaypoint is not null)
        {
            results.Add(new GeneratedTrackLeg(
                startWaypoint.Id,
                nextWaypoint.Id,
                totalDistanceCm,
                markers.Count == 0 ? null : markers.OrderBy(marker => marker.OffsetCm).ThenBy(marker => marker.SensorId).ToList().AsReadOnly()));
            return;
        }

        var nodeId = moveForward ? edge.ToNodeId : edge.FromNodeId;
        if (nodeWaypoints.TryGetValue(nodeId, out var nodeWaypoint) &&
            !string.Equals(nodeWaypoint.Id, startWaypoint.Id, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new GeneratedTrackLeg(
                startWaypoint.Id,
                nodeWaypoint.Id,
                totalDistanceCm,
                markers.Count == 0 ? null : markers.OrderBy(marker => marker.OffsetCm).ThenBy(marker => marker.SensorId).ToList().AsReadOnly()));
            return;
        }

        if (!edgesByNode.TryGetValue(nodeId, out var outgoingEdges))
            return;

        foreach (var nextEdge in outgoingEdges)
        {
            if (string.Equals(nextEdge.Id, edge.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            var nextMoveForward = string.Equals(nextEdge.FromNodeId, nodeId, StringComparison.OrdinalIgnoreCase);
            ExploreBranch(
                startWaypoint,
                nextEdge,
                nextMoveForward,
                nextMoveForward ? 0 : nextEdge.LengthCm,
                includeCurrentPositionSensors: true,
                enteredFromNodeBoundary: true,
                distanceFromStartCm: totalDistanceCm,
                currentMarkers: markers,
                visitedStates: new HashSet<string>(visitedStates, StringComparer.OrdinalIgnoreCase),
                edgesByNode,
                edgeWaypoints,
                nodeWaypoints,
                sectionsByEdge,
                pointsByEdge,
                results);
        }
    }

    private static TrackWaypoint? FindNextWaypointOnEdge(
        string startWaypointId,
        string edgeId,
        int startOffsetCm,
        bool moveForward,
        IReadOnlyDictionary<string, List<TrackWaypoint>> edgeWaypoints,
        out int? nextWaypointOffsetCm)
    {
        nextWaypointOffsetCm = null;
        if (!edgeWaypoints.TryGetValue(edgeId, out var waypointsOnEdge))
            return null;

        if (moveForward)
        {
            foreach (var waypoint in waypointsOnEdge)
            {
                if (string.Equals(waypoint.Id, startWaypointId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (waypoint.OffsetCm!.Value <= startOffsetCm)
                    continue;

                nextWaypointOffsetCm = waypoint.OffsetCm.Value;
                return waypoint;
            }

            return null;
        }

        for (var i = waypointsOnEdge.Count - 1; i >= 0; i--)
        {
            var waypoint = waypointsOnEdge[i];
            if (string.Equals(waypoint.Id, startWaypointId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (waypoint.OffsetCm!.Value >= startOffsetCm)
                continue;

            nextWaypointOffsetCm = waypoint.OffsetCm.Value;
            return waypoint;
        }

        return null;
    }

    private static void AppendSensorsOnEdge(
        TrackEdge edge,
        bool moveForward,
        int startOffsetCm,
        int endOffsetCm,
        int distanceFromStartCm,
        bool includeCurrentPositionSensors,
        bool enteredFromNodeBoundary,
        IReadOnlyDictionary<string, List<TrackFeedbackSection>> sectionsByEdge,
        IReadOnlyDictionary<string, List<TrackFeedbackPoint>> pointsByEdge,
        ICollection<SensorMarker> markers)
    {
        if (enteredFromNodeBoundary && sectionsByEdge.TryGetValue(edge.Id, out var sections))
        {
            foreach (var section in sections.OrderBy(section => section.DetectorId))
            {
                markers.Add(new SensorMarker(section.DetectorId, distanceFromStartCm, section.Type));
            }
        }

        if (!pointsByEdge.TryGetValue(edge.Id, out var points))
            return;

        foreach (var point in points)
        {
            var include = moveForward
                ? (includeCurrentPositionSensors ? point.OffsetCm >= startOffsetCm : point.OffsetCm > startOffsetCm) && point.OffsetCm < endOffsetCm
                : (includeCurrentPositionSensors ? point.OffsetCm <= startOffsetCm : point.OffsetCm < startOffsetCm) && point.OffsetCm > endOffsetCm;
            if (!include)
                continue;

            var relativeOffsetCm = distanceFromStartCm + Math.Abs(point.OffsetCm - startOffsetCm);
            markers.Add(new SensorMarker(point.DetectorId, relativeOffsetCm, point.Type));
        }
    }

    private static TrackEdge ParseEdge(XElement edgeElement, string elementKind, string parentElementId)
    {
        var id = RequireAttribute(edgeElement, "id", elementKind);
        var fromNodeId = RequireAttribute(edgeElement, "from", $"{elementKind} '{id}'");
        var toNodeId = RequireAttribute(edgeElement, "to", $"{elementKind} '{id}'");
        var lengthCm = ParseIntAttribute(edgeElement, "length_cm")
                       ?? throw new RouteValidationException($"{elementKind} '{id}' requires length_cm.");
        if (lengthCm <= 0)
            throw new RouteValidationException($"{elementKind} '{id}' requires length_cm > 0.");
        if (string.Equals(fromNodeId, toNodeId, StringComparison.OrdinalIgnoreCase))
            throw new RouteValidationException($"{elementKind} '{id}' requires distinct from/to nodes.");

        return new TrackEdge(id, parentElementId.Trim(), fromNodeId, toNodeId, lengthCm);
    }

    private static TrackEdge GetRequiredEdge(
        IReadOnlyDictionary<string, TrackEdge> edges,
        string hostTrackId,
        string context)
    {
        if (edges.TryGetValue(hostTrackId.Trim(), out var edge))
            return edge;

        throw new RouteValidationException($"{context} references unknown host track '{hostTrackId}'.");
    }

    private static bool NodeExists(IReadOnlyDictionary<string, TrackEdge> edges, string nodeId)
    {
        return edges.Values.Any(edge =>
            string.Equals(edge.FromNodeId, nodeId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(edge.ToNodeId, nodeId, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveHostTrackId(XElement element, string context)
    {
        var hostTrackId = OptionalAttribute(element, "host")
                          ?? OptionalAttribute(element, "track")
                          ?? OptionalAttribute(element, "segment")
                          ?? OptionalAttribute(element, "segment_id")
                          ?? OptionalAttribute(element, "path")
                          ?? OptionalAttribute(element, "path_id");
        return !string.IsNullOrWhiteSpace(hostTrackId)
            ? hostTrackId.Trim()
            : throw new RouteValidationException($"{context} requires host='...'.");
    }

    private static IReadOnlyList<string> ResolveSectionHostEdgeIds(
        IReadOnlyDictionary<string, TrackEdge> edges,
        string hostTrackId,
        string context)
    {
        if (edges.TryGetValue(hostTrackId.Trim(), out var directEdge))
            return [directEdge.Id];

        var matchedEdgeIds = edges.Values
            .Where(edge => string.Equals(edge.ParentElementId, hostTrackId, StringComparison.OrdinalIgnoreCase))
            .Select(edge => edge.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matchedEdgeIds.Count > 0)
            return matchedEdgeIds;

        throw new RouteValidationException($"{context} references unknown host track or path '{hostTrackId}'.");
    }

    private static IReadOnlyList<string> ResolveHostEdgeIdsForOffset(
        IReadOnlyDictionary<string, TrackEdge> edges,
        string hostTrackId,
        int offsetCm,
        string context)
    {
        var hostEdges = ResolveSectionHostEdgeIds(edges, hostTrackId, context);
        var compatibleEdges = hostEdges
            .Where(edgeId =>
            {
                var edge = GetRequiredEdge(edges, edgeId, context);
                return offsetCm >= 0 && offsetCm <= edge.LengthCm;
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (compatibleEdges.Count > 0)
            return compatibleEdges;

        var maxLength = hostEdges
            .Select(edgeId => GetRequiredEdge(edges, edgeId, context).LengthCm)
            .DefaultIfEmpty(0)
            .Max();
        throw new RouteValidationException(
            $"{context} must be within [0, {maxLength}] cm of host '{hostTrackId}'.");
    }


    private static string NormalizeKey(string value) => value.Trim().ToUpperInvariant();

    private static string RequireAttribute(XElement element, string name, string context)
    {
        var value = OptionalAttribute(element, name);
        return !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new RouteValidationException($"Missing required attribute '{name}' in {context}.");
    }

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

    private sealed record LayoutSnapshot(
        Dictionary<(string From, string To), GeneratedTrackLeg> GeneratedLegs,
        Dictionary<string, TrackFeedbackSection> Sections,
        Dictionary<string, TrackFeedbackPoint> Points,
        Dictionary<string, TrackWaypoint> Waypoints);

    private sealed record TrackEdge(
        string Id,
        string ParentElementId,
        string FromNodeId,
        string ToNodeId,
        int LengthCm);

    private sealed record InitialBranch(
        TrackEdge Edge,
        bool MoveForward,
        int StartOffsetCm,
        bool IncludeCurrentPositionSensors,
        bool EnteredFromNodeBoundary);
}
