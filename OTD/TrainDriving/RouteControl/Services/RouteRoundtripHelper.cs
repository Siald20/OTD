// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using OTD.TrainDriving.RouteControl.Domain;

namespace OTD.TrainDriving.RouteControl.Services;

/// <summary>
/// Helper fuer die Validierung, ob definierte RouteLeg-Eintraege eine geschlossene Runde bilden.
/// </summary>
public static class RouteRoundtripHelper
{
    public static RoundtripValidationResult ValidateCompleteRound(
        IRouteDefinitionService definitionService,
        string startWaypointId,
        bool requireDefaultSpeed = true,
        bool requireAllEligibleLegsUsed = true)
    {
        ArgumentNullException.ThrowIfNull(definitionService);
        ArgumentException.ThrowIfNullOrWhiteSpace(startWaypointId);

        var normalizedStart = startWaypointId.Trim();
        var allSegments = definitionService.GetAllSegments();
        var eligibleSegments = (requireDefaultSpeed
            ? allSegments.Where(segment =>
                segment.MaxSpeedByClassKmh.TryGetValue("default", out var speedKmh) && speedKmh > 0)
            : allSegments)
            .ToList();

        if (eligibleSegments.Count == 0)
        {
            return RoundtripValidationResult.Failed(
                "Keine pruefbaren RouteSegmente gefunden (ggf. fehlen Default-Geschwindigkeiten).",
                eligibleSegments,
                Array.Empty<RouteSegment>());
        }

        var outgoingByFrom = eligibleSegments
            .GroupBy(segment => segment.FromWaypointId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var consumed = new List<RouteSegment>();
        var consumedKeys = new HashSet<(string From, string To)>();
        var current = normalizedStart;

        for (var step = 0; step <= eligibleSegments.Count; step++)
        {
            if (!outgoingByFrom.TryGetValue(current, out var outgoing) || outgoing.Count == 0)
            {
                return RoundtripValidationResult.Failed(
                    $"Luecke in der Runde: Kein RouteSegment-Eintrag mit from='{current}'.",
                    eligibleSegments,
                    consumed);
            }

            if (outgoing.Count > 1)
            {
                var options = string.Join(", ", outgoing.Select(segment => $"{segment.FromWaypointId}->{segment.ToWaypointId}"));
                return RoundtripValidationResult.Failed(
                    $"Mehrdeutig ab '{current}': mehrere ausgehende Segmente ({options}).",
                    eligibleSegments,
                    consumed);
            }

            var segment = outgoing[0];
            var segmentKey = (NormalizeKey(segment.FromWaypointId), NormalizeKey(segment.ToWaypointId));
            if (!consumedKeys.Add(segmentKey))
            {
                return RoundtripValidationResult.Failed(
                    $"Zyklus entdeckt, bevor der Startpunkt erreicht wurde: '{segment.FromWaypointId}->{segment.ToWaypointId}'.",
                    eligibleSegments,
                    consumed);
            }

            consumed.Add(segment);
            current = segment.ToWaypointId.Trim();

            if (string.Equals(current, normalizedStart, StringComparison.OrdinalIgnoreCase))
            {
                if (requireAllEligibleLegsUsed && consumed.Count != eligibleSegments.Count)
                {
                    var consumedSet = new HashSet<(string From, string To)>(consumedKeys);
                    var unconsumed = eligibleSegments
                        .Where(candidate => !consumedSet.Contains((NormalizeKey(candidate.FromWaypointId), NormalizeKey(candidate.ToWaypointId))))
                        .Select(candidate => $"{candidate.FromWaypointId}->{candidate.ToWaypointId}")
                        .ToArray();

                    return RoundtripValidationResult.Failed(
                        $"Runde ist zwar geschlossen, aber nicht vollstaendig: {unconsumed.Length} Eintrag/Eintraege ungenutzt ({string.Join(", ", unconsumed)}).",
                        eligibleSegments,
                        consumed);
                }

                return RoundtripValidationResult.Succeeded(consumed);
            }
        }

        return RoundtripValidationResult.Failed(
            "Runde konnte nicht geschlossen werden (Abbruch durch Schrittlimit).",
            eligibleSegments,
            consumed);
    }

    private static string NormalizeKey(string value) => value.Trim().ToUpperInvariant();
}

public sealed record RoundtripValidationResult(
    bool IsCompleteRound,
    string Message,
    IReadOnlyList<RouteSegment> EligibleSegments,
    IReadOnlyList<RouteSegment> RoundSegments)
{
    public static RoundtripValidationResult Succeeded(IReadOnlyList<RouteSegment> roundSegments)
        => new(
            IsCompleteRound: true,
            Message: "Vollstaendige geschlossene Runde gefunden.",
            EligibleSegments: roundSegments,
            RoundSegments: roundSegments);

    public static RoundtripValidationResult Failed(
        string message,
        IReadOnlyList<RouteSegment> eligibleSegments,
        IReadOnlyList<RouteSegment> roundSegments)
        => new(
            IsCompleteRound: false,
            Message: message,
            EligibleSegments: eligibleSegments,
            RoundSegments: roundSegments);
}

