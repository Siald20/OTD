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

        if (leg.SensorMarkers is null)
            return;

        var localSensorIds = new HashSet<int>();
        foreach (var marker in leg.SensorMarkers)
        {
            if (marker.OffsetCm < 0 || marker.OffsetCm > leg.DistanceCm)
            {
                throw new RouteValidationException(
                    $"SensorMarker '{marker.SensorId}' on RouteLeg '{leg.FromWaypointId}' must be within [0, {leg.DistanceCm}] cm.");
            }

            if (!localSensorIds.Add(marker.SensorId))
                throw new RouteValidationException($"Duplicate SensorMarker '{marker.SensorId}' on RouteLeg '{leg.FromWaypointId}'.");
        }
    }

    public static void ValidateChain(IReadOnlyList<RouteLeg> legs)
    {
        var fromWaypointSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var globalSensorIds = new HashSet<int>();

        for (var i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            ValidateLeg(leg);

            if (!fromWaypointSet.Add(leg.FromWaypointId.Trim()))
                throw new RouteValidationException($"Duplicate FromWaypointId '{leg.FromWaypointId}'.");

            if (leg.SensorMarkers is not null)
            {
                foreach (var marker in leg.SensorMarkers)
                {
                    if (!globalSensorIds.Add(marker.SensorId))
                    {
                        throw new RouteValidationException(
                            $"SensorMarker '{marker.SensorId}' is not unique across RouteTable.");
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

