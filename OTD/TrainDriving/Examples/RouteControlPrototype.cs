// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using OTD.TrainDriving.RouteControl.Builder;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.Trajectory;

namespace OTD.TrainDriving.Examples;

/// <summary>
/// Small in-code route definition example for position tracking.
/// </summary>
public static class RouteControlPrototype
{
    public static IReadOnlyList<RouteLeg> CreateRouteTable()
    {
        return new RouteTableBuilder()
            .AddRoute(
                fromWaypointId: "S0",
                toWaypointId: "S1",
                distanceCm: 55,
                maxSpeedKmh: 40,
                accelerationPreset: AccelerationTrajectoryPreset.EarlyAcceleration,
                brakingPreset: BrakingTrajectoryPreset.LateBrake)
            .AddRoute(
                fromWaypointId: "S1",
                toWaypointId: "S2",
                distanceCm: 40,
                maxSpeedKmh: 20,
                brakingPreset: BrakingTrajectoryPreset.LateBrake)
            .AddRoute(
                fromWaypointId: "S2",
                toWaypointId: "S3",
                distanceCm: 25,
                maxSpeedKmh: 15)
            .AddStopPoint(fromWaypointId: "S2", offsetCm: 20, stopReason: "Demo-Halt")
            .Build();
    }
}

