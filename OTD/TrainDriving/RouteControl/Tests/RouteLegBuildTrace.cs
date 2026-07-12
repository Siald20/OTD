// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Exceptions;
using OTD.TrainDriving.RouteControl.Services;

namespace OTD.TrainDriving.RouteControl.Tests;

public static class RouteLegBuildTrace
{
    // Fixed trace parameters (no environment-variable dependency)
    private const string TraceFromWaypointId = "S_C2";
    private const string TraceToWaypointId = "S_E104";
    private static readonly int? TraceMaxSpeedKmh = null;
    private static string? _traceSpeedClass = null;

    // Source configuration for the trace run
    private static readonly bool UseMinimalSampleData = false;

    public static void RunInteractive()
    {
        var useMinimal = UseMinimalSampleData;

        string? tempRoot = null;
        string selectedLayoutPath;
        string selectedSegmentsPath;
        XmlRailwayLayoutService layout;
        XmlRouteDefinitionService definitions;
        if (useMinimal)
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "otd-route-trace-minimal", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            selectedLayoutPath = Path.Combine(tempRoot, "railwaylayout.xml");
            selectedSegmentsPath = Path.Combine(tempRoot, "topology.xml");
            File.WriteAllText(selectedLayoutPath, BuildMinimalLayoutXml());
            File.WriteAllText(selectedSegmentsPath, BuildMinimalTopologyXml());

            layout = new XmlRailwayLayoutService(selectedLayoutPath, selectedSegmentsPath);
            definitions = new XmlRouteDefinitionService(layout, selectedSegmentsPath);
        }
        else
        {
            // Non-minimal mode always reads production AppData XML files.
            selectedLayoutPath = XmlRailwayLayoutService.GetDefaultConfigFilePath();
            selectedSegmentsPath = XmlRouteDefinitionService.GetDefaultConfigFilePath();
            layout = new XmlRailwayLayoutService(selectedLayoutPath, selectedSegmentsPath);
            definitions = new XmlRouteDefinitionService(layout, selectedSegmentsPath);
        }

        try
        {
            Console.WriteLine("=== RouteLeg Trace Config ===");
            Console.WriteLine($"Layout XML: {selectedLayoutPath}");
            Console.WriteLine($"RouteSegments XML: {selectedSegmentsPath}");
            Console.WriteLine();
            if (TraceMaxSpeedKmh is null && string.IsNullOrWhiteSpace(_traceSpeedClass))
                Run(TraceFromWaypointId, TraceToWaypointId, definitions: definitions, layout: layout);
            else if (TraceMaxSpeedKmh is null)
                Run(TraceFromWaypointId, TraceToWaypointId, definitions: definitions, layout: layout, speedClass: _traceSpeedClass);
            else if (string.IsNullOrWhiteSpace(_traceSpeedClass))
                Run(TraceFromWaypointId, TraceToWaypointId, TraceMaxSpeedKmh, definitions, layout);
            else
                Run(TraceFromWaypointId, TraceToWaypointId, TraceMaxSpeedKmh, definitions, layout, _traceSpeedClass);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempRoot) && Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, recursive: true); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Trace] Temp cleanup failed: {ex.Message}");
                }
            }
        }
    }

    public static void Run(
        string fromWaypointId,
        string toWaypointId,
        int? maxSpeedKmh = null,
        IRouteDefinitionService? definitions = null,
        IRailwayLayoutService? layout = null,
        string? speedClass = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromWaypointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toWaypointId);

        var effectiveSpeedClass = string.IsNullOrWhiteSpace(speedClass) ? "default" : speedClass.Trim();
        var requestedMaxSpeedKmh = maxSpeedKmh ?? int.MaxValue;

        var effectiveLayout = layout ?? new XmlRailwayLayoutService(
            topologyFilePath: XmlRailwayLayoutService.GetDefaultTopologyFilePath());
        var effectiveDefinitions = definitions ?? new XmlRouteDefinitionService(effectiveLayout);
        var resolver = new RouteLegResolver(effectiveDefinitions, effectiveLayout);

        Console.WriteLine("=== RouteLeg Build Trace ===");
        var maxLabel = maxSpeedKmh is null ? "segment-only" : $"{maxSpeedKmh.Value:F0} km/h";
        Console.WriteLine($"Input: from={fromWaypointId}, to={toWaypointId}, max={maxLabel}, speedClass={effectiveSpeedClass}");

        var segmentPath = ResolvePath(fromWaypointId.Trim(), toWaypointId.Trim(), effectiveLayout, effectiveDefinitions);
        Console.WriteLine($"Resolved segment count: {segmentPath.Count}");

        var cumulativeDistanceCm = 0;
        for (var i = 0; i < segmentPath.Count; i++)
        {
            var edge = segmentPath[i];
            if (!TryResolveRouteSegment(effectiveDefinitions, edge.FromWaypointId, edge.ToWaypointId, out var segment))
            {
                throw new RouteValidationException(
                    $"No RouteSegment found for '{edge.FromWaypointId}<->{edge.ToWaypointId}'.");
            }

            var speedClassSummary = string.Join(", ",
                segment.MaxSpeedByClassKmh
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => $"{pair.Key}={pair.Value}"));
            var resolvedClassSpeed = segment.ResolveSpeedLimitKmh(effectiveSpeedClass);

            Console.WriteLine(
                $"  [{i + 1}] {edge.FromWaypointId}->{edge.ToWaypointId}, len={segment.LengthCm} cm, speedclasses=[{speedClassSummary}], selected={effectiveSpeedClass}:{resolvedClassSpeed}");

            if (edge.FeedbackActivationPoints is not null && edge.FeedbackActivationPoints.Count > 0)
            {
                foreach (var marker in edge.FeedbackActivationPoints.OrderBy(marker => marker.OffsetCm).ThenBy(marker => marker.FeedbackId))
                {
                    Console.WriteLine(
                        $"      feedback@segment id={marker.FeedbackId}, offset={marker.OffsetCm} cm, type={marker.Type}, offset@routeleg={marker.OffsetCm + cumulativeDistanceCm} cm");
                }
            }
            else
            {
                Console.WriteLine("      feedback@segment: -");
            }

            cumulativeDistanceCm += segment.LengthCm;
        }

        var draftLeg = new RouteLeg(
            TravelDirection: RouteTravelDirection.AlongLine,
            FromWaypointId: fromWaypointId.Trim(),
            ToWaypointId: toWaypointId.Trim(),
            MaxSpeedKmh: requestedMaxSpeedKmh);
        var resolvedLeg = resolver.Resolve(draftLeg, effectiveSpeedClass);

        Console.WriteLine();
        Console.WriteLine("Resolved RouteLeg:");
        Console.WriteLine($"  from={resolvedLeg.FromWaypointId}, to={resolvedLeg.ToWaypointId}");
        Console.WriteLine($"  distance={resolvedLeg.DistanceCm} cm");
        Console.WriteLine($"  maxSpeed(effective)={resolvedLeg.MaxSpeedKmh:F0} km/h");
        Console.WriteLine($"  driveProfile={(resolvedLeg.DriveProfile is null ? "-" : "set")}");
        Console.WriteLine($"  stopPoint={(resolvedLeg.StopPoint is null ? "-" : resolvedLeg.StopPoint.OffsetCm + " cm")}");

        Console.WriteLine("  FeedbackActivationPoints:");
        if (resolvedLeg.FeedbackInputActivationPoints is null || resolvedLeg.FeedbackInputActivationPoints.Count == 0)
        {
            Console.WriteLine("    -");
        }
        else
        {
            foreach (var marker in resolvedLeg.FeedbackInputActivationPoints.OrderBy(marker => marker.OffsetCm).ThenBy(marker => marker.FeedbackId))
            {
                Console.WriteLine($"    id={marker.FeedbackId}, offset={marker.OffsetCm} cm, type={marker.Type}");
            }
        }

        Console.WriteLine("  FeedbackReferences:");
        if (resolvedLeg.FeedbackInputReferences is null || resolvedLeg.FeedbackInputReferences.Count == 0)
        {
            Console.WriteLine("    -");
        }
        else
        {
            foreach (var reference in resolvedLeg.FeedbackInputReferences.OrderBy(reference => reference.OffsetCm))
            {
                Console.WriteLine($"    target={reference.TargetId}, kind={reference.TargetKind}, offset={reference.OffsetCm} cm");
            }
        }
    }

    private static List<GeneratedTrackLeg> ResolvePath(
        string fromWaypointId,
        string toWaypointId,
        IRailwayLayoutService layout,
        IRouteDefinitionService definitions)
    {
        if (string.Equals(fromWaypointId, toWaypointId, StringComparison.OrdinalIgnoreCase))
            throw new RouteValidationException("FromWaypointId and ToWaypointId must be different.");

        var allGenerated = layout.GetAllGeneratedLegs();
        var eligibleEdges = allGenerated
            .Where(edge => TryResolveRouteSegment(definitions, edge.FromWaypointId, edge.ToWaypointId, out _))
            .ToList();

        var byFrom = eligibleEdges
            .GroupBy(edge => edge.FromWaypointId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        if (!byFrom.ContainsKey(fromWaypointId))
        {
            throw new RouteValidationException(
                $"No outgoing routesegment edges configured for start waypoint '{fromWaypointId}'.");
        }

        var dist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var prev = new Dictionary<string, GeneratedTrackLeg>(StringComparer.OrdinalIgnoreCase);
        var queue = new PriorityQueue<string, int>();

        dist[fromWaypointId] = 0;
        queue.Enqueue(fromWaypointId, 0);

        while (queue.TryDequeue(out var node, out var nodeDist))
        {
            if (dist.TryGetValue(node, out var known) && nodeDist > known)
                continue;

            if (string.Equals(node, toWaypointId, StringComparison.OrdinalIgnoreCase))
                break;

            if (!byFrom.TryGetValue(node, out var outgoing))
                continue;

            foreach (var edge in outgoing)
            {
                var next = edge.ToWaypointId.Trim();
                var candidate = nodeDist + edge.DistanceCm;
                if (dist.TryGetValue(next, out var existing) && candidate >= existing)
                    continue;

                dist[next] = candidate;
                prev[next] = edge;
                queue.Enqueue(next, candidate);
            }
        }

        if (!prev.ContainsKey(toWaypointId))
        {
            throw new RouteValidationException(
                $"No routesegment path found from '{fromWaypointId}' to '{toWaypointId}'.");
        }

        var path = new List<GeneratedTrackLeg>();
        var cursor = toWaypointId;
        while (!string.Equals(cursor, fromWaypointId, StringComparison.OrdinalIgnoreCase))
        {
            if (!prev.TryGetValue(cursor, out var edge))
            {
                throw new RouteValidationException(
                    $"Failed to reconstruct routesegment path from '{fromWaypointId}' to '{toWaypointId}'.");
            }

            path.Add(edge);
            cursor = edge.FromWaypointId.Trim();
        }

        path.Reverse();
        return path;
    }

    private static bool TryResolveRouteSegment(
        IRouteDefinitionService definitions,
        string fromWaypointId,
        string toWaypointId,
        out RouteSegment segment)
    {
        return definitions.TryGetSegment(fromWaypointId, toWaypointId, out segment)
               || definitions.TryGetSegment(toWaypointId, fromWaypointId, out segment);
    }


    private static string BuildMinimalLayoutXml()
    {
        return """
               <railwaylayout version="2">
                   <trackelements>
                       <trackelement id="seg_A" type="simpletrack" from="nA" to="nW" length_cm="150"/>
                       <trackelement id="SW1" type="turnout">
                           <path id="sw1_main" from="nW" to="nB1" length_cm="95"/>
                           <path id="sw1_branch" from="nW" to="nB2" length_cm="110"/>
                       </trackelement>
                   </trackelements>
                   <feedbacks>
                       <occupancy id="sec_A" detectorId="30" host="seg_A"/>
                       <contact id="pt_145" detectorId="145" host="seg_A" offset_cm="145"/>
                       <occupancy id="sec_branch" detectorId="32" host="sw1_branch"/>
                   </feedbacks>
               </railwaylayout>
               """;
    }

    private static string BuildMinimalTopologyXml()
    {
        return """
               <topology version="1">
                   <waypoints>
                       <waypoint id="A" node="nA"/>
                       <waypoint id="MID" host="seg_A" offset_cm="60"/>
                       <waypoint id="W" node="nW"/>
                       <waypoint id="B1" node="nB1"/>
                       <waypoint id="B2" node="nB2"/>
                   </waypoints>
                   <segments>
                       <segment from="A" to="MID">
                           <speedlimits>
                               <speedlimit speedClass="default" speed_kmh="40"/>
                               <speedlimit speedClass="freight" speed_kmh="35"/>
                           </speedlimits>
                       </segment>
                       <segment from="MID" to="W">
                           <speedlimits>
                               <speedlimit speedClass="default" speed_kmh="50"/>
                           </speedlimits>
                       </segment>
                       <segment from="W" to="B1">
                           <speedlimits>
                               <speedlimit speedClass="default" speed_kmh="60"/>
                           </speedlimits>
                       </segment>
                       <segment from="W" to="B2">
                           <speedlimits>
                               <speedlimit speedClass="default" speed_kmh="30"/>
                           </speedlimits>
                       </segment>
                   </segments>
               </topology>
               """;
    }
}




