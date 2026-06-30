// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Services;
using OTD.TrainDriving.Trajectory;

namespace OTD.TrainDriving.RouteControl.Builder;

public sealed class RouteTableBuilder
{
    private readonly List<RouteLeg> _routeLegs = new();

    public IReadOnlyList<RouteLeg> RouteLegs => new ReadOnlyCollection<RouteLeg>(_routeLegs);

    public RouteTableBuilder AddRoute(
        string fromWaypointId,
        string toWaypointId,
        int distanceCm,
        double maxSpeedKmh,
        AccelerationStartPolicy accelerationStartPolicy = AccelerationStartPolicy.AtWaypointCrossing,
        AccelerationTrajectoryPreset? accelerationPreset = null,
        BrakingTrajectoryPreset? brakingPreset = null)
    {
        RouteDriveProfile? driveProfile = null;
        if (accelerationPreset is not null || brakingPreset is not null)
        {
            driveProfile = new RouteDriveProfile(
                AccelerationPreset: accelerationPreset,
                BrakingPreset: brakingPreset);
        }

        var leg = new RouteLeg(
            FromWaypointId: fromWaypointId,
            ToWaypointId: toWaypointId,
            DistanceCm: distanceCm,
            MaxSpeedKmh: maxSpeedKmh,
            DriveProfile: driveProfile,
            AccelerationStartPolicy: accelerationStartPolicy);

        RouteValidator.ValidateLeg(leg);
        _routeLegs.Add(leg);
        return this;
    }

    public RouteTableBuilder AddStopPoint(string fromWaypointId, int offsetCm, string? stopReason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromWaypointId);

        var index = IndexOfFromWaypoint(fromWaypointId);
        var leg = _routeLegs[index] with
        {
            StopPoint = new StopPoint(OffsetCm: offsetCm, StopReason: stopReason)
        };

        RouteValidator.ValidateLeg(leg);
        _routeLegs[index] = leg;
        return this;
    }

    public IReadOnlyList<RouteLeg> Build()
    {
        RouteValidator.ValidateChain(_routeLegs);
        return new ReadOnlyCollection<RouteLeg>(new List<RouteLeg>(_routeLegs));
    }

    private int IndexOfFromWaypoint(string fromWaypointId)
    {
        var normalized = fromWaypointId.Trim();
        var index = -1;

        for (var i = 0; i < _routeLegs.Count; i++)
        {
            if (!string.Equals(_routeLegs[i].FromWaypointId, normalized, StringComparison.OrdinalIgnoreCase))
                continue;

            if (index >= 0)
                throw new Exceptions.RouteValidationException($"fromWaypointId '{fromWaypointId}' is not unique in builder state.");

            index = i;
        }

        if (index < 0)
            throw new Exceptions.RouteValidationException($"Unknown fromWaypointId '{fromWaypointId}' in builder state.");

        return index;
    }
}

