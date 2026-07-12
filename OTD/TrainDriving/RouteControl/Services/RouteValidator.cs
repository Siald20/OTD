// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Exceptions;

namespace OTD.TrainDriving.RouteControl.Services;

public static class RouteValidator
{
    public static void ValidateLeg(RouteLeg leg)
    {
        if (string.IsNullOrWhiteSpace(leg.FromWaypointId))
            throw new RouteValidationException("RouteLeg requires FromWaypointId.");

        if (string.IsNullOrWhiteSpace(leg.ToWaypointId))
            throw new RouteValidationException("RouteLeg requires ToWaypointId.");

        if (string.Equals(leg.FromWaypointId.Trim(), leg.ToWaypointId.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new RouteValidationException("RouteLeg requires different FromWaypointId and ToWaypointId.");

        if (leg.DistanceCm <= 0)
            throw new RouteValidationException("RouteLeg requires DistanceCm > 0.");

        if (leg.MaxSpeedKmh <= 0)
            throw new RouteValidationException("RouteLeg requires MaxSpeedKmh > 0.");

        if (leg.StopPoint is not null && (leg.StopPoint.OffsetCm < 0 || leg.StopPoint.OffsetCm > leg.DistanceCm))
        {
            throw new RouteValidationException(
                $"StopPoint on RouteLeg '{leg.FromWaypointId}' must be within [0, {leg.DistanceCm}] cm.");
        }

        if (leg.FeedbackInputActivationPoints is not null)
        {
            var localFeedbackIds = new HashSet<int>();
            foreach (var marker in leg.FeedbackInputActivationPoints)
            {
                if (marker.OffsetCm < 0 || marker.OffsetCm > leg.DistanceCm)
                {
                    throw new RouteValidationException(
                        $"FeedbackActivationPoint '{marker.FeedbackId}' on RouteLeg '{leg.FromWaypointId}' must be within [0, {leg.DistanceCm}] cm.");
                }

                if (!localFeedbackIds.Add(marker.FeedbackId))
                    throw new RouteValidationException($"Duplicate FeedbackActivationPoint '{marker.FeedbackId}' on RouteLeg '{leg.FromWaypointId}'.");
            }
        }

        if (leg.FeedbackInputReferences is null)
            return;

        var localFeedbackReferenceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in leg.FeedbackInputReferences)
        {
            if (reference.OffsetCm < 0 || reference.OffsetCm > leg.DistanceCm)
            {
                throw new RouteValidationException(
                    $"FeedbackReference '{reference.TargetId}' on RouteLeg '{leg.FromWaypointId}' must be within [0, {leg.DistanceCm}] cm.");
            }

            if (!localFeedbackReferenceIds.Add(reference.TargetId.Trim()))
                throw new RouteValidationException($"Duplicate FeedbackReference '{reference.TargetId}' on RouteLeg '{leg.FromWaypointId}'.");
        }
    }

    public static void ValidateChain(IReadOnlyList<RouteLeg> legs)
    {
        for (var i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            ValidateLeg(leg);


            // Feedback IDs may repeat across legs when one physical detector is mapped
            // to multiple logical route anchors (e.g., direction-specific section entries).

            if (leg.FeedbackInputReferences is not null)
            {
                foreach (var reference in leg.FeedbackInputReferences)
                {
                    if (reference.OffsetCm < 0 || reference.OffsetCm > leg.DistanceCm)
                    {
                        throw new RouteValidationException(
                            $"FeedbackReference '{reference.TargetId}' on RouteLeg '{leg.FromWaypointId}' must be within [0, {leg.DistanceCm}] cm.");
                    }
                }
            }

            if (i == 0)
                continue;

            var previous = legs[i - 1];
            if (!string.Equals(previous.ToWaypointId.Trim(), leg.FromWaypointId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new RouteValidationException(
                    $"Route chain break between '{previous.FromWaypointId}->{previous.ToWaypointId}' and '{leg.FromWaypointId}->{leg.ToWaypointId}'.");
            }
        }
    }
}

