// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using OTD.TrainDriving.RouteControl.Builder;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Services;
using OTD.TrainDriving.Trajectory;

namespace OTD.TrainDriving.Examples;

/// <summary>
/// Small in-code route definition example for position tracking.
/// </summary>
public static class RouteControlPrototype
{
    public static IReadOnlyList<RouteLeg> CreateRouteTable(
        IRouteDefinitionService routeDefinitionService,
        IRailwayLayoutService railwayLayoutService)
    {
        return new RouteTableBuilder(routeDefinitionService, railwayLayoutService)
            .AddRoute(
                fromWaypointId: "S0",
                toWaypointId: "S1",
                maxSpeedKmh: 40,
                accelerationPreset: AccelerationTrajectoryPreset.EarlyAcceleration,
                brakingPreset: BrakingTrajectoryPreset.LateBrake)
            .AddRoute(
                fromWaypointId: "S1",
                toWaypointId: "S2",
                maxSpeedKmh: 20,
                brakingPreset: BrakingTrajectoryPreset.LateBrake)
            .AddRoute(
                fromWaypointId: "S2",
                toWaypointId: "S3",
                maxSpeedKmh: 15)
            .AddStopPointToTarget(fromWaypointId: "S2", stopPointToTargetCm: 20, stopReason: "Demo-Halt")
            .Build();
    }
}
