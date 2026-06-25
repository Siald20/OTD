// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using OTD.TrainDriving.RouteModel;

namespace OTD.TrainDriving.Examples;

/// <summary>
/// Minimal demo for route modeling with free position events.
/// </summary>
public static class RouteRuntimeActionsDemo
{
    public static void RunDemo()
    {
        var route = new RouteTableBuilder()
            .AddRoute(
                id: 1,
                fromWaypointId: "S1",
                toWaypointId: "S2",
                distanceCm: 120,
                startRoutePermission: RoutePermission.Proceed(maxSpeedKmh: 40, aspect: "Warnung"))
            .AddRoute(
                id: 2,
                fromWaypointId: "S2",
                toWaypointId: "S3",
                distanceCm: 180,
                startRoutePermission: RoutePermission.Stop())
            .AddSensorMarker(routeId: 1, sensorId: 1, offsetCm: 30)
            .AddSensorMarker(routeId: 2, sensorId: 2, offsetCm: 70)
            .AddActionEvent(
                eventId: "HX_BUE_01",
                type: RouteActionType.WarnHorn,
                positionCm: 150,
                payload: "Warnhorn vor unbewachtem Bahnuebergang",
                triggerOnce: true)
            .Build();

        var runtime = new RouteRuntime(route);

        var tick1 = runtime.ApplyStep(deltaCm: 80, trajectorySpeedKmh: 70);
        Console.WriteLine($"Tick1 pos={tick1.EstimatedPositionCm:F1} cmd={tick1.EffectiveSpeedKmh}");

        var tick2 = runtime.ApplyStep(deltaCm: 80, trajectorySpeedKmh: 70, activatedSensorId: 2);
        Console.WriteLine(
            $"Tick2 pos={tick2.EstimatedPositionCm:F1} cmd={tick2.EffectiveSpeedKmh} " +
            $"events={tick2.TriggeredActions.Count} recal={tick2.PositionRecalibrated}");
    }
}



