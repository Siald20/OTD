// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using OTD.Common;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Exceptions;

namespace OTD.TrainDriving.RouteControl.Services;

public sealed class RouteLegResolver
{
    private readonly IRouteDefinitionService _definitionService;
    private readonly IRailwayLayoutService? _trackLayoutService;

    public RouteLegResolver(IRouteDefinitionService definitionService, IRailwayLayoutService? trackLayoutService = null)
    {
        _definitionService = definitionService ?? throw new ArgumentNullException(nameof(definitionService));
        _trackLayoutService = trackLayoutService;
    }

    public RouteLeg Resolve(DynamicRouteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestLeg = new RouteLeg(
            FromWaypointId: request.FromWaypointId,
            ToWaypointId: request.ToWaypointId,
            MaxSpeedKmh: request.MaxSpeedKmh ?? int.MaxValue,
            DriveProfile: request.DriveProfile,
            StopPointToTargetCm: request.StopPointToTargetCm,
            Metadata: request.Metadata);

        if (request.AccelerationStartPolicy is not null)
            requestLeg = requestLeg with { AccelerationStartPolicy = request.AccelerationStartPolicy.Value };

        var expanded = ExpandInternal(requestLeg, speedClass: "default");
        return AggregateExpanded(requestLeg, expanded);
    }

    public IReadOnlyList<RouteLeg> Expand(DynamicRouteRequest request, string speedClass = "default")
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(speedClass);

        var requestLeg = new RouteLeg(
            FromWaypointId: request.FromWaypointId,
            ToWaypointId: request.ToWaypointId,
            MaxSpeedKmh: request.MaxSpeedKmh ?? int.MaxValue,
            DriveProfile: request.DriveProfile,
            StopPointToTargetCm: request.StopPointToTargetCm,
            Metadata: request.Metadata);

        if (request.AccelerationStartPolicy is not null)
            requestLeg = requestLeg with { AccelerationStartPolicy = request.AccelerationStartPolicy.Value };

        return ExpandInternal(requestLeg, speedClass.Trim());
    }

    public RouteLeg Resolve(RouteLeg leg)
    {
        return Resolve(leg, speedClass: "default");
    }

    public RouteLeg Resolve(RouteLeg leg, string speedClass)
    {
        ArgumentNullException.ThrowIfNull(leg);
        ArgumentException.ThrowIfNullOrWhiteSpace(speedClass);
        if (leg.IsTopologyResolved)
            return leg;

        var expanded = ExpandInternal(leg, speedClass.Trim());
        return AggregateExpanded(leg, expanded);
    }

    public IReadOnlyList<RouteLeg> Expand(RouteLeg leg, string speedClass = "default")
    {
        ArgumentNullException.ThrowIfNull(leg);
        ArgumentException.ThrowIfNullOrWhiteSpace(speedClass);
        if (leg.IsTopologyResolved)
            return [leg];

        return ExpandInternal(leg, speedClass.Trim());
    }

    private IReadOnlyList<RouteLeg> ExpandInternal(RouteLeg requestLeg, string speedClass)
    {
        var path = ResolvePath(requestLeg.FromWaypointId, requestLeg.ToWaypointId);
        if (path.Count == 0)
        {
            throw new RouteValidationException(
                $"No topology path found for '{requestLeg.FromWaypointId}->{requestLeg.ToWaypointId}'.");
        }

        var totalDistanceCm = 0;
        foreach (var segment in path)
            totalDistanceCm += ResolveRouteSegment(segment.FromWaypointId, segment.ToWaypointId).LengthCm;

        var requestedMax = requestLeg.MaxSpeedKmh <= 0 ? int.MaxValue : requestLeg.MaxSpeedKmh;
        var requestedMaxLabel = FormatRequestedMaxSpeed(requestedMax);
        var groupId = Guid.NewGuid().ToString("N");
        var routeLegCorrelation = $"RouteLeg={requestLeg.FromWaypointId}->{requestLeg.ToWaypointId} Group={groupId}";

        Logging.Debug<RouteLegResolver>(
            $"Event=RouteLegBuildStart {routeLegCorrelation} SpeedClass={speedClass} RequestedMax={requestedMaxLabel} " +
            $"SegmentCount={path.Count} TotalDistanceCm={totalDistanceCm} Path={FormatPath(path)}");

        int? stopPointAbsoluteCm = null;
        if (requestLeg.StopPointToTargetCm is not null)
        {
            var relativeToTarget = requestLeg.StopPointToTargetCm.Value;
            if (relativeToTarget < 0)
            {
                throw new RouteValidationException(
                    $"RouteLeg '{requestLeg.FromWaypointId}->{requestLeg.ToWaypointId}' requires StopPointToTargetCm >= 0.");
            }

            stopPointAbsoluteCm = Math.Clamp(totalDistanceCm - relativeToTarget, 0, totalDistanceCm);
        }

        var expanded = new List<RouteLeg>(path.Count);
        var cumulativeDistanceCm = 0;

        foreach (var segment in path)
        {
            var routeSegment = ResolveRouteSegment(segment.FromWaypointId, segment.ToWaypointId);
            var segmentLengthCm = routeSegment.LengthCm;
            var segmentCapKmh = routeSegment.ResolveSpeedLimitKmh(speedClass);
            var effectiveMaxSpeedKmh = Math.Min(segmentCapKmh, requestedMax);
            if (effectiveMaxSpeedKmh <= 0)
                effectiveMaxSpeedKmh = segmentCapKmh;

            StopPoint? runtimeStopPoint = null;
            if (stopPointAbsoluteCm is not null)
            {
                var segmentStart = cumulativeDistanceCm;
                var segmentEnd = cumulativeDistanceCm + segmentLengthCm;
                if (stopPointAbsoluteCm.Value >= segmentStart && stopPointAbsoluteCm.Value <= segmentEnd)
                {
                    runtimeStopPoint = new StopPoint(
                        OffsetCm: stopPointAbsoluteCm.Value - segmentStart,
                        StopReason: "RouteLeg target-relative stop");
                }
            }

            var segmentMarkers = segment.SensorMarkers is null
                ? null
                : segment.SensorMarkers
                    .OrderBy(marker => marker.OffsetCm)
                    .ThenBy(marker => marker.SensorId)
                    .Select(marker => new SensorMarker(marker.SensorId, marker.OffsetCm, marker.Type))
                    .ToList()
                    .AsReadOnly();

            var usesReverseDefinition = !string.Equals(routeSegment.FromWaypointId, segment.FromWaypointId, StringComparison.OrdinalIgnoreCase) ||
                                        !string.Equals(routeSegment.ToWaypointId, segment.ToWaypointId, StringComparison.OrdinalIgnoreCase);
            Logging.Debug<RouteLegResolver>(
                $"Event=RouteLegSegmentBuild {routeLegCorrelation} SegmentIndex={expanded.Count + 1}/{path.Count} " +
                $"PathSegment={segment.FromWaypointId}->{segment.ToWaypointId} DefinitionSegment={routeSegment.FromWaypointId}->{routeSegment.ToWaypointId} " +
                $"DefinitionDirection={(usesReverseDefinition ? "reverse" : "forward")} SegmentLengthCm={segmentLengthCm} " +
                $"SpeedClass={speedClass} TopologyMaxKmh={segmentCapKmh} RequestedMax={requestedMaxLabel} EffectiveMaxKmh={effectiveMaxSpeedKmh} " +
                $"SpeedClasses={FormatSpeedClassSummary(routeSegment)} Sensors={FormatSensorSummary(segmentMarkers)} StopPoint={FormatStopPoint(runtimeStopPoint)}");

            var expandedLeg = new RouteLeg(
                FromWaypointId: segment.FromWaypointId,
                ToWaypointId: segment.ToWaypointId,
                MaxSpeedKmh: effectiveMaxSpeedKmh,
                DriveProfile: requestLeg.DriveProfile,
                StopPointToTargetCm: null,
                Metadata: requestLeg.Metadata)
            {
                DistanceCm = segmentLengthCm,
                MaxSpeedKmh = effectiveMaxSpeedKmh,
                AccelerationStartPolicy = requestLeg.AccelerationStartPolicy,
                StopPoint = runtimeStopPoint,
                SensorMarkers = segmentMarkers,
                FeedbackReferences = null,
                GroupId = groupId,
                GroupTotalDistanceCm = totalDistanceCm,
                GroupOffsetStartCm = cumulativeDistanceCm
            };

            RouteValidator.ValidateLeg(expandedLeg);
            expanded.Add(expandedLeg);
            cumulativeDistanceCm += segmentLengthCm;
        }

        var effectiveRouteLegMaxKmh = expanded.Min(leg => leg.MaxSpeedKmh);
        var totalSensorCount = expanded.Sum(leg => leg.SensorMarkers?.Count ?? 0);
        var sensorPositions = FormatRouteLegSensorPositions(expanded);
        Logging.Info<RouteLegResolver>(
            $"Event=RouteLegCreated {routeLegCorrelation} SpeedClass={speedClass} SegmentCount={expanded.Count} DistanceCm={totalDistanceCm} " +
            $"EffectiveMaxKmh={effectiveRouteLegMaxKmh} RequestedMax={requestedMaxLabel} SensorCount={totalSensorCount} " +
            $"SensorPositions={sensorPositions} StopPoint={FormatStopPoint(stopPointAbsoluteCm)}");

        return expanded.AsReadOnly();
    }

    private static RouteLeg AggregateExpanded(RouteLeg requestLeg, IReadOnlyList<RouteLeg> expanded)
    {
        if (expanded.Count == 0)
            throw new RouteValidationException($"No expanded RouteSegments found for '{requestLeg.FromWaypointId}->{requestLeg.ToWaypointId}'.");

        var totalDistanceCm = expanded.Sum(leg => leg.DistanceCm);
        var effectiveMaxSpeed = expanded.Min(leg => leg.MaxSpeedKmh);

        var aggregateMarkers = new List<SensorMarker>();
        var runningOffset = 0;
        foreach (var leg in expanded)
        {
            if (leg.SensorMarkers is not null)
            {
                aggregateMarkers.AddRange(leg.SensorMarkers.Select(marker =>
                    new SensorMarker(marker.SensorId, marker.OffsetCm + runningOffset, marker.Type)));
            }

            runningOffset += leg.DistanceCm;
        }

        StopPoint? aggregateStopPoint = null;
        if (requestLeg.StopPointToTargetCm is not null)
        {
            var target = Math.Clamp(totalDistanceCm - requestLeg.StopPointToTargetCm.Value, 0, totalDistanceCm);
            aggregateStopPoint = new StopPoint(target, "RouteLeg target-relative stop");
        }

        var resolvedLeg = requestLeg with
        {
            FromWaypointId = expanded[0].FromWaypointId,
            ToWaypointId = expanded[^1].ToWaypointId,
            DistanceCm = totalDistanceCm,
            MaxSpeedKmh = effectiveMaxSpeed,
            StopPoint = aggregateStopPoint,
            SensorMarkers = aggregateMarkers.Count == 0
                ? null
                : aggregateMarkers.OrderBy(marker => marker.OffsetCm).ThenBy(marker => marker.SensorId).ToList().AsReadOnly(),
            FeedbackReferences = null,
            GroupId = expanded[0].GroupId,
            GroupTotalDistanceCm = totalDistanceCm,
            GroupOffsetStartCm = 0
        };

        RouteValidator.ValidateLeg(resolvedLeg);
        return resolvedLeg;
    }

    private RouteSegment ResolveRouteSegment(string fromWaypointId, string toWaypointId)
    {
        if (_definitionService.TryGetSegment(fromWaypointId, toWaypointId, out var directSegment))
            return directSegment;

        if (_definitionService.TryGetSegment(toWaypointId, fromWaypointId, out var reverseSegment))
            return reverseSegment;

        throw new RouteValidationException(
            $"No routesegment override found for neighboring waypoints '{fromWaypointId}<->{toWaypointId}'.");
    }

    private List<GeneratedTrackLeg> ResolvePath(string fromWaypointId, string toWaypointId)
    {
        if (_trackLayoutService is null)
        {
            throw new RouteValidationException(
                "Topology-based RouteLeg composition requires an IRailwayLayoutService. Construct RouteLegResolver(definitionService, trackLayoutService)." );
        }

        var normalizedFrom = fromWaypointId.Trim();
        var normalizedTo = toWaypointId.Trim();
        if (string.Equals(normalizedFrom, normalizedTo, StringComparison.OrdinalIgnoreCase))
            throw new RouteValidationException("RouteLeg requires different FromWaypointId and ToWaypointId.");

        var allGenerated = _trackLayoutService.GetAllGeneratedLegs();
        var eligibleEdges = allGenerated
            .Where(leg => HasSegmentDefinition(leg.FromWaypointId, leg.ToWaypointId))
            .ToList();

        var byFrom = eligibleEdges
            .GroupBy(edge => edge.FromWaypointId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        if (!byFrom.ContainsKey(normalizedFrom))
        {
            throw new RouteValidationException(
                $"No outgoing routesegment edges configured for start waypoint '{fromWaypointId}'.");
        }

        var dist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var prev = new Dictionary<string, GeneratedTrackLeg>(StringComparer.OrdinalIgnoreCase);
        var queue = new PriorityQueue<string, int>();

        dist[normalizedFrom] = 0;
        queue.Enqueue(normalizedFrom, 0);

        while (queue.TryDequeue(out var node, out var nodeDist))
        {
            if (dist.TryGetValue(node, out var known) && nodeDist > known)
                continue;

            if (string.Equals(node, normalizedTo, StringComparison.OrdinalIgnoreCase))
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

        if (!prev.ContainsKey(normalizedTo))
        {
            throw new RouteValidationException(
                $"No routesegment path found from '{fromWaypointId}' to '{toWaypointId}'.");
        }

        var path = new List<GeneratedTrackLeg>();
        var cursor = normalizedTo;
        while (!string.Equals(cursor, normalizedFrom, StringComparison.OrdinalIgnoreCase))
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

    private bool HasSegmentDefinition(string fromWaypointId, string toWaypointId)
    {
        return _definitionService.TryGetSegment(fromWaypointId, toWaypointId, out _) ||
               _definitionService.TryGetSegment(toWaypointId, fromWaypointId, out _);
    }

    private static string FormatRequestedMaxSpeed(int requestedMaxKmh)
    {
        return requestedMaxKmh == int.MaxValue ? "segment-only" : $"{requestedMaxKmh} km/h";
    }

    private static string FormatSpeedClassSummary(RouteSegment routeSegment)
    {
        return string.Join(", ",
            routeSegment.MaxSpeedByClassKmh
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private static string FormatPath(IReadOnlyList<GeneratedTrackLeg> path)
    {
        return string.Join(" | ", path.Select(segment => $"{segment.FromWaypointId}->{segment.ToWaypointId}({segment.DistanceCm} cm)"));
    }

    private static string FormatSensorSummary(IReadOnlyList<SensorMarker>? sensorMarkers)
    {
        if (sensorMarkers is null || sensorMarkers.Count == 0)
            return "-";

        return string.Join(", ", sensorMarkers.Select(marker => $"{marker.SensorId}@{marker.OffsetCm}cm/{marker.Type}"));
    }

    private static string FormatRouteLegSensorPositions(IReadOnlyList<RouteLeg> expanded)
    {
        if (expanded.Count == 0)
            return "-";

        var sensors = new List<string>();
        var cumulativeOffsetCm = 0;

        foreach (var leg in expanded)
        {
            if (leg.SensorMarkers is not null)
            {
                foreach (var marker in leg.SensorMarkers.OrderBy(marker => marker.OffsetCm).ThenBy(marker => marker.SensorId))
                {
                    var absoluteOffsetCm = cumulativeOffsetCm + marker.OffsetCm;
                    sensors.Add($"{marker.SensorId}@{absoluteOffsetCm}cm/{marker.Type}/{leg.FromWaypointId}->{leg.ToWaypointId}");
                }
            }

            cumulativeOffsetCm += leg.DistanceCm;
        }

        return sensors.Count == 0 ? "-" : string.Join(";", sensors);
    }

    private static string FormatStopPoint(StopPoint? stopPoint)
    {
        return stopPoint is null ? "-" : $"{stopPoint.OffsetCm} cm";
    }

    private static string FormatStopPoint(int? stopPointOffsetCm)
    {
        return stopPointOffsetCm is null ? "-" : $"{stopPointOffsetCm.Value} cm";
    }

}

