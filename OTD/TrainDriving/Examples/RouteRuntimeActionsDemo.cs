// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using OTD.TrainDriving.RouteControl.Builder;
using OTD.TrainDriving.RouteControl.Services;

namespace OTD.TrainDriving.Examples;

/// <summary>
/// Minimal demo for RouteControl stop-point behavior.
/// </summary>
public static class RouteRuntimeActionsDemo
{
    public static void RunDemo(IRouteDefinitionService routeDefinitionService, IRailwayLayoutService railwayLayoutService)
    {
        var legs = new RouteTableBuilder(routeDefinitionService, railwayLayoutService)
            .AddRoute(
                fromWaypointId: "S1",
                toWaypointId: "S2",
                maxSpeedKmh: 40)
             .AddRoute(
                fromWaypointId: "S2",
                toWaypointId: "S3",
                maxSpeedKmh: 25)
            .AddStopPointToTarget(fromWaypointId: "S2", stopPointToTargetCm: 15, stopReason: "Bahnsteig")
            .Build();
 
        var service = new RouteTableService();
        service.AddRoutes(legs);
 
        service.AdvancePosition(125);
        var s1 = service.GetRuntimeState();
        Console.WriteLine($"Tick1 pos={s1.HeadPositionCm:F1} active={s1.ActiveFromWaypointId} stop={s1.ActiveStopPoint}");
 
        service.ReleaseGo();
        service.AdvancePosition(140);
        var s2 = service.GetRuntimeState();
        Console.WriteLine($"Tick2 pos={s2.HeadPositionCm:F1} active={s2.ActiveFromWaypointId} stop={s2.ActiveStopPoint}");
    }
}
