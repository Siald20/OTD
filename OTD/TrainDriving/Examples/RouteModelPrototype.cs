// SPDX-License-Identifier: GPL-3.0-or-later

using OTD.TrainDriving.RouteModel;
using OTD.TrainDriving.Presets;

namespace OTD.TrainDriving.Examples;

/// <summary>
/// Small in-code route definition example for position tracking.
/// </summary>
public static class RouteModelPrototype
{
    public static RouteTable CreateRouteTable()
    {
        return new RouteTableBuilder()
            .AddRoute(
                id: 1,
                fromWaypointId: "S0",
                toWaypointId: "S1",
                distanceCm: 55,
                startRoutePermission: RoutePermission.Proceed(maxSpeedKmh: 40, aspect: "Fahrt"),
                accelerationPreset: AccelerationTrajectoryPreset.EarlyAcceleration,
                brakingPreset: BrakingTrajectoryPreset.LateBrake,
                block: "SA1")
            .AddRoute(
                id: 2,
                fromWaypointId: "S1",
                toWaypointId: "S2",
                distanceCm: 40,
                startRoutePermission: RoutePermission.Proceed(maxSpeedKmh: 20, aspect: "Warnung"),
                brakingPreset: BrakingTrajectoryPreset.LateBrake,
                block: "W2")
            .AddRoute(
                id: 3,
                fromWaypointId: "S2",
                toWaypointId: "S3",
                distanceCm: 175,
                startRoutePermission: RoutePermission.Stop(),
                block: "SA2")
            .AddSensorMarker(routeId: 1, sensorId: 1, offsetCm: 5)
            .AddSensorMarker(routeId: 2, sensorId: 2, offsetCm: 20)
            .AddSensorMarker(routeId: 3, sensorId: 4, offsetCm: 120)
            .Build();
    }
}


