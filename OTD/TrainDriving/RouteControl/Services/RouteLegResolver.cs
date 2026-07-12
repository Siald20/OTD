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
            Metadata: request.Metadata,
            TravelDirection: request.TravelDirection);

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
            Metadata: request.Metadata,
            TravelDirection: request.TravelDirection);

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

    public int ResolvePathDistanceCm(string fromWaypointId, string toWaypointId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromWaypointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toWaypointId);

        var path = ResolvePath(fromWaypointId.Trim(), toWaypointId.Trim());
        var totalDistanceCm = 0;
        foreach (var segment in path)
            totalDistanceCm += ResolveRouteSegment(segment.FromWaypointId, segment.ToWaypointId).LengthCm;

        return totalDistanceCm;
    }

    private IReadOnlyList<RouteLeg> ExpandInternal(RouteLeg requestLeg, string speedClass)
    {
        // From/To are always the literal physical start and end of the train run,
        // as provided by the interlocking (which has its own per-direction route buttons).
        // ResolvePath uses a bidirectional graph and finds the correct path directly,
        // regardless of TravelDirection — no inversion needed.
        var path = ResolvePath(requestLeg.FromWaypointId, requestLeg.ToWaypointId);

        // Build a lookup of all generated legs keyed by directed From/To for feedback resolution.
        var generatedByDirectedKey = BuildDirectedGeneratedLegLookup();

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

            var segmentMarkers = ResolveSegmentMarkers(
                segment, routeSegment.LengthCm, generatedByDirectedKey);

            var usesReverseDefinition = !string.Equals(routeSegment.FromWaypointId, segment.FromWaypointId, StringComparison.OrdinalIgnoreCase) ||
                                        !string.Equals(routeSegment.ToWaypointId, segment.ToWaypointId, StringComparison.OrdinalIgnoreCase);
            Logging.Debug<RouteLegResolver>(
                $"Event=RouteLegSegmentBuild {routeLegCorrelation} SegmentIndex={expanded.Count + 1}/{path.Count} " +
                $"PathSegment={segment.FromWaypointId}->{segment.ToWaypointId} DefinitionSegment={routeSegment.FromWaypointId}->{routeSegment.ToWaypointId} " +
                $"DefinitionDirection={(usesReverseDefinition ? "reverse" : "forward")} SegmentLengthCm={segmentLengthCm} " +
                $"SpeedClass={speedClass} TopologyMaxKmh={segmentCapKmh} RequestedMax={requestedMaxLabel} EffectiveMaxKmh={effectiveMaxSpeedKmh} " +
                $"SpeedClasses={FormatSpeedClassSummary(routeSegment)} Feedbacks={FormatFeedbackSummary(segmentMarkers)} StopPoint={FormatStopPoint(runtimeStopPoint)}");

            var expandedLeg = new RouteLeg(
                FromWaypointId: segment.FromWaypointId,
                ToWaypointId: segment.ToWaypointId,
                MaxSpeedKmh: effectiveMaxSpeedKmh,
                DriveProfile: requestLeg.DriveProfile,
                StopPointToTargetCm: null,
                Metadata: requestLeg.Metadata,
                TravelDirection: requestLeg.TravelDirection)
            {
                DistanceCm = segmentLengthCm,
                MaxSpeedKmh = effectiveMaxSpeedKmh,
                AccelerationStartPolicy = requestLeg.AccelerationStartPolicy,
                StopPoint = runtimeStopPoint,
                FeedbackInputActivationPoints = segmentMarkers,
                FeedbackInputReferences = null,
                GroupId = groupId,
                GroupTotalDistanceCm = totalDistanceCm,
                GroupOffsetStartCm = cumulativeDistanceCm
            };

            RouteValidator.ValidateLeg(expandedLeg);
            expanded.Add(expandedLeg);
            cumulativeDistanceCm += segmentLengthCm;
        }

        var effectiveRouteLegMaxKmh = expanded.Min(leg => leg.MaxSpeedKmh);
        var totalFeedbackCount = expanded.Sum(leg => leg.FeedbackInputActivationPoints?.Count ?? 0);
        var feedbackPositions = FormatRouteLegFeedbackPositions(expanded);
        Logging.Info<RouteLegResolver>(
            $"Event=RouteLegCreated {routeLegCorrelation} SpeedClass={speedClass} SegmentCount={expanded.Count} DistanceCm={totalDistanceCm} " +
            $"EffectiveMaxKmh={effectiveRouteLegMaxKmh} RequestedMax={requestedMaxLabel} FeedbackCount={totalFeedbackCount} " +
            $"FeedbackPositions={feedbackPositions} StopPoint={FormatStopPoint(stopPointAbsoluteCm)}");

        return expanded.AsReadOnly();
    }

    private static RouteLeg AggregateExpanded(RouteLeg requestLeg, IReadOnlyList<RouteLeg> expanded)
    {
        if (expanded.Count == 0)
            throw new RouteValidationException($"No expanded RouteSegments found for '{requestLeg.FromWaypointId}->{requestLeg.ToWaypointId}'.");

        var totalDistanceCm = expanded.Sum(leg => leg.DistanceCm);
        var effectiveMaxSpeed = expanded.Min(leg => leg.MaxSpeedKmh);

        var aggregateMarkers = new List<FeedbackActivationPoint>();
        var runningOffset = 0;
        foreach (var leg in expanded)
        {
            if (leg.FeedbackInputActivationPoints is not null)
            {
                aggregateMarkers.AddRange(leg.FeedbackInputActivationPoints.Select(marker =>
                    new FeedbackActivationPoint(marker.FeedbackId, marker.OffsetCm + runningOffset, marker.Type)));
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
            FeedbackInputActivationPoints = aggregateMarkers.Count == 0
                ? null
                : aggregateMarkers.OrderBy(marker => marker.OffsetCm).ThenBy(marker => marker.FeedbackId).ToList().AsReadOnly(),
            FeedbackInputReferences = null,
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

        // Add reverse edges for bidirectional support: if a segment has an override,
        // both directions should be traversable with the same override (but inverted markers).
        var reverseEdges = eligibleEdges
            .Select(edge => new GeneratedTrackLeg(
                FromWaypointId: edge.ToWaypointId,
                ToWaypointId: edge.FromWaypointId,
                DistanceCm: edge.DistanceCm,
                FeedbackActivationPoints: edge.FeedbackActivationPoints is null
                    ? null
                    : edge.FeedbackActivationPoints
                        .Select(m => new FeedbackActivationPoint(
                            m.FeedbackId,
                            Math.Clamp(edge.DistanceCm - m.OffsetCm, 0, edge.DistanceCm),
                            m.Type))
                        .OrderBy(m => m.OffsetCm)
                        .ThenBy(m => m.FeedbackId)
                        .ToList()
                        .AsReadOnly()))
            .ToList();

        var allEdges = eligibleEdges.Concat(reverseEdges).Distinct().ToList();
        
        var byFrom = allEdges
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

    private static string FormatFeedbackSummary(IReadOnlyList<FeedbackActivationPoint>? feedbackMarkers)
    {
        if (feedbackMarkers is null || feedbackMarkers.Count == 0)
            return "-";

        return string.Join(", ", feedbackMarkers.Select(marker => $"{marker.FeedbackId}@{marker.OffsetCm}cm/{marker.Type}"));
    }

    private static string FormatRouteLegFeedbackPositions(IReadOnlyList<RouteLeg> expanded)
    {
        if (expanded.Count == 0)
            return "-";

        var feedbacks = new List<string>();
        var cumulativeOffsetCm = 0;

        foreach (var leg in expanded)
        {
            if (leg.FeedbackInputActivationPoints is not null)
            {
                foreach (var marker in leg.FeedbackInputActivationPoints.OrderBy(marker => marker.OffsetCm).ThenBy(marker => marker.FeedbackId))
                {
                    var absoluteOffsetCm = cumulativeOffsetCm + marker.OffsetCm;
                    feedbacks.Add($"{marker.FeedbackId}@{absoluteOffsetCm}cm/{marker.Type}/{leg.FromWaypointId}->{leg.ToWaypointId}");
                }
            }

            cumulativeOffsetCm += leg.DistanceCm;
        }

        return feedbacks.Count == 0 ? "-" : string.Join(";", feedbacks);
    }

    private static string FormatStopPoint(StopPoint? stopPoint)
    {
        return stopPoint is null ? "-" : $"{stopPoint.OffsetCm} cm";
    }

    private static string FormatStopPoint(int? stopPointOffsetCm)
    {
        return stopPointOffsetCm is null ? "-" : $"{stopPointOffsetCm.Value} cm";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Direction helpers
    // ──────────────────────────────────────────────────────────────────────────


    /// <summary>
    /// Builds a lookup of all generated legs keyed by normalized directed (From, To).
    /// When multiple legs exist for the same directed pair, the shortest is kept.
    /// </summary>
    private IReadOnlyDictionary<(string From, string To), GeneratedTrackLeg> BuildDirectedGeneratedLegLookup()
    {
        if (_trackLayoutService is null)
            return new Dictionary<(string From, string To), GeneratedTrackLeg>();

        var result = new Dictionary<(string From, string To), GeneratedTrackLeg>();
        foreach (var leg in _trackLayoutService.GetAllGeneratedLegs())
        {
            var key = (NormKey(leg.FromWaypointId), NormKey(leg.ToWaypointId));
            if (!result.TryGetValue(key, out var existing) || leg.DistanceCm < existing.DistanceCm)
                result[key] = leg;
        }

        return result;
    }

    /// <summary>
    /// Resolves FeedbackActivationPoints for a path segment, taking into account that the
    /// generated leg may have a different distance from the topology RouteSegment length.
    ///
    /// Strategy:
    /// 1. If the segment's DistanceCm matches the RouteSegment length → use as-is.
    /// 2. Try the same directed generated leg (From→To); if its length matches → use it.
    /// 3. Try the opposite directed generated leg (To→From); if its length matches
    ///    → use it with mirrored offsets.
    /// 4. Fall back to no feedbacks (avoids validation failure).
    /// </summary>
    private static IReadOnlyList<FeedbackActivationPoint>? ResolveSegmentMarkers(
        GeneratedTrackLeg segment,
        int routeSegmentLengthCm,
        IReadOnlyDictionary<(string From, string To), GeneratedTrackLeg> generatedByDirectedKey)
    {
        // Case 1: the segment already has the correct length.
        if (segment.DistanceCm == routeSegmentLengthCm)
            return NormalizeMarkers(segment.FeedbackActivationPoints, routeSegmentLengthCm);

        var directKey = (NormKey(segment.FromWaypointId), NormKey(segment.ToWaypointId));
        var reverseKey = (NormKey(segment.ToWaypointId), NormKey(segment.FromWaypointId));

        // Case 2: a generated leg in the same direction with the correct length.
        if (generatedByDirectedKey.TryGetValue(directKey, out var directLeg) &&
            directLeg.DistanceCm == routeSegmentLengthCm)
        {
            return NormalizeMarkers(directLeg.FeedbackActivationPoints, routeSegmentLengthCm);
        }

        // Case 3: the opposite direction has the correct length → mirror offsets.
        if (generatedByDirectedKey.TryGetValue(reverseKey, out var reverseLeg) &&
            reverseLeg.DistanceCm == routeSegmentLengthCm)
        {
            var mirrored = reverseLeg.FeedbackActivationPoints is null
                ? null
                : reverseLeg.FeedbackActivationPoints
                    .Select(m => new FeedbackActivationPoint(
                        m.FeedbackId,
                        Math.Clamp(routeSegmentLengthCm - m.OffsetCm, 0, routeSegmentLengthCm),
                        m.Type))
                    .OrderBy(m => m.OffsetCm)
                    .ThenBy(m => m.FeedbackId)
                    .ToList()
                    .AsReadOnly();
            return mirrored;
        }

        // Case 4: no matching generated leg found — omit feedbacks to avoid validation failure.
        return null;
    }

    private static IReadOnlyList<FeedbackActivationPoint>? NormalizeMarkers(
        IReadOnlyList<FeedbackActivationPoint>? markers,
        int maxOffsetCm)
    {
        if (markers is null || markers.Count == 0)
            return null;

        return markers
            .Where(m => m.OffsetCm >= 0 && m.OffsetCm <= maxOffsetCm)
            .OrderBy(m => m.OffsetCm)
            .ThenBy(m => m.FeedbackId)
            .Select(m => new FeedbackActivationPoint(m.FeedbackId, m.OffsetCm, m.Type))
            .ToList()
            .AsReadOnly();
    }

    private static string NormKey(string value) => value.Trim().ToUpperInvariant();

}

